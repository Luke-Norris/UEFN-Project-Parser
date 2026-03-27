"""HTTP listener and main-thread tick dispatch for the WellVersed bridge.

Architecture
------------
UEFN's Python runs on the editor's main thread. All ``unreal.*`` API calls
MUST happen there. HTTP requests arrive on a background thread, so we use:

1. ``http.server.HTTPServer`` on a daemon thread receives requests.
2. Each request is put into a ``queue.Queue()``.
3. ``unreal.register_slate_post_tick_callback()`` drains the queue every
   editor tick (on the main thread) and calls command handlers.
4. Results are stored in a shared dict; the HTTP thread polls until the
   result arrives (30 s timeout, 20 ms poll interval).

Shared state is stored as attributes on the ``unreal`` module so it survives
script re-execution inside the editor.
"""

from __future__ import annotations

import json
import queue
import socket
import threading
import time
import uuid
from http.server import HTTPServer, BaseHTTPRequestHandler
from typing import Any, Callable

import unreal

# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

_PORTS = [9220, 9221, 9222]
_POLL_INTERVAL = 0.020   # 20 ms
_POLL_TIMEOUT = 30.0     # 30 s
_MAX_COMMANDS_PER_TICK = 5

# ---------------------------------------------------------------------------
# Command registry
# ---------------------------------------------------------------------------

COMMAND_REGISTRY: dict[str, Callable[..., Any]] = {}


def register_command(name: str):
    """Decorator that registers a callable as a bridge command.

    Usage::

        @register_command("my_command")
        def my_command(params: dict) -> dict:
            ...
    """
    def decorator(fn: Callable[..., Any]):
        COMMAND_REGISTRY[name] = fn
        return fn
    return decorator


# ---------------------------------------------------------------------------
# Shared state helpers
# ---------------------------------------------------------------------------

def _ensure_shared_state():
    """Initialise shared state on the ``unreal`` module if not present."""
    if not hasattr(unreal, '_wellversed_queue'):
        unreal._wellversed_queue = queue.Queue()
    if not hasattr(unreal, '_wellversed_responses'):
        unreal._wellversed_responses = {}
    if not hasattr(unreal, '_wellversed_server'):
        unreal._wellversed_server = None
    if not hasattr(unreal, '_wellversed_thread'):
        unreal._wellversed_thread = None
    if not hasattr(unreal, '_wellversed_tick_handle'):
        unreal._wellversed_tick_handle = None
    if not hasattr(unreal, '_wellversed_port'):
        unreal._wellversed_port = None


def _get_queue() -> queue.Queue:
    return unreal._wellversed_queue


def _get_responses() -> dict:
    return unreal._wellversed_responses


# ---------------------------------------------------------------------------
# Tick handler (runs on editor main thread)
# ---------------------------------------------------------------------------

def _tick_handler(delta_time: float):
    """Drain the command queue on the editor's main thread.

    Called every editor tick via ``register_slate_post_tick_callback``.
    Processes up to ``_MAX_COMMANDS_PER_TICK`` commands per tick to avoid
    blocking the editor.
    """
    q = _get_queue()
    responses = _get_responses()

    for _ in range(_MAX_COMMANDS_PER_TICK):
        try:
            request_id, command, params = q.get_nowait()
        except queue.Empty:
            break

        try:
            handler = COMMAND_REGISTRY.get(command)
            if handler is None:
                responses[request_id] = {
                    "status": "error",
                    "error": f"Unknown command: {command}",
                }
            else:
                result = handler(params)
                responses[request_id] = {
                    "status": "ok",
                    "result": result,
                }
        except Exception as exc:
            unreal.log_warning(
                f"[WellVersed] Command '{command}' failed: {exc}"
            )
            responses[request_id] = {
                "status": "error",
                "error": str(exc),
            }


# ---------------------------------------------------------------------------
# HTTP request handler
# ---------------------------------------------------------------------------

class _BridgeHandler(BaseHTTPRequestHandler):
    """Minimal HTTP handler for WellVersed bridge requests."""

    # Suppress default stderr logging
    def log_message(self, format, *args):
        pass

    # -- GET endpoints ------------------------------------------------------

    def do_GET(self):
        if self.path == "/status":
            self._send_json({
                "status": "ok",
                "bridge": "wellversed",
                "version": "1.0.0",
                "port": unreal._wellversed_port,
                "commands": sorted(COMMAND_REGISTRY.keys()),
            })
        elif self.path == "/commands":
            self._send_json({
                "status": "ok",
                "commands": sorted(COMMAND_REGISTRY.keys()),
            })
        else:
            self._send_json({"status": "error", "error": "Not found"}, 404)

    # -- POST endpoint (command dispatch) -----------------------------------

    def do_POST(self):
        if self.path != "/command":
            self._send_json({"status": "error", "error": "Not found"}, 404)
            return

        content_length = int(self.headers.get("Content-Length", 0))
        if content_length == 0:
            self._send_json(
                {"status": "error", "error": "Empty request body"}, 400
            )
            return

        try:
            body = json.loads(self.rfile.read(content_length))
        except json.JSONDecodeError as exc:
            self._send_json(
                {"status": "error", "error": f"Invalid JSON: {exc}"}, 400
            )
            return

        command = body.get("command")
        params = body.get("params", {})
        request_id = body.get("request_id") or str(uuid.uuid4())

        if not command:
            self._send_json(
                {"status": "error", "error": "Missing 'command' field"}, 400
            )
            return

        # Enqueue for main-thread execution
        _get_queue().put((request_id, command, params))

        # Poll for result
        responses = _get_responses()
        deadline = time.monotonic() + _POLL_TIMEOUT
        while time.monotonic() < deadline:
            if request_id in responses:
                result = responses.pop(request_id)
                result["request_id"] = request_id
                self._send_json(result)
                return
            time.sleep(_POLL_INTERVAL)

        self._send_json(
            {
                "status": "error",
                "error": "Timed out waiting for main-thread execution",
                "request_id": request_id,
            },
            504,
        )

    # -- CORS preflight -----------------------------------------------------

    def do_OPTIONS(self):
        self.send_response(204)
        self._set_cors_headers()
        self.end_headers()

    # -- Helpers ------------------------------------------------------------

    def _send_json(self, data: dict, status: int = 200):
        body = json.dumps(data, default=str).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self._set_cors_headers()
        self.end_headers()
        self.wfile.write(body)

    def _set_cors_headers(self):
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type")


# ---------------------------------------------------------------------------
# Port scanning
# ---------------------------------------------------------------------------

def _find_available_port() -> int | None:
    """Find the first available port from the candidate list."""
    for port in _PORTS:
        try:
            with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
                sock.bind(("127.0.0.1", port))
                return port
        except OSError:
            continue
    return None


# ---------------------------------------------------------------------------
# Public API
# ---------------------------------------------------------------------------

def start_listener():
    """Start the HTTP server and register the tick callback.

    Safe to call multiple times -- will stop any existing listener first.
    """
    _ensure_shared_state()

    # Stop any existing listener
    if unreal._wellversed_server is not None:
        stop_listener()

    # Import commands so they register themselves
    from . import commands as _commands  # noqa: F401

    port = _find_available_port()
    if port is None:
        unreal.log_error(
            "[WellVersed] Could not find an available port "
            f"(tried {_PORTS})"
        )
        return

    server = HTTPServer(("127.0.0.1", port), _BridgeHandler)
    server.timeout = 0.5

    thread = threading.Thread(
        target=server.serve_forever,
        name="WellVersed-HTTP",
        daemon=True,
    )
    thread.start()

    tick_handle = unreal.register_slate_post_tick_callback(_tick_handler)

    # Store state on unreal module
    unreal._wellversed_server = server
    unreal._wellversed_thread = thread
    unreal._wellversed_tick_handle = tick_handle
    unreal._wellversed_port = port

    unreal.log(f"[WellVersed] Bridge started on http://127.0.0.1:{port}")


def stop_listener():
    """Shut down the HTTP server and unregister the tick callback."""
    _ensure_shared_state()

    if unreal._wellversed_tick_handle is not None:
        try:
            unreal.unregister_slate_post_tick_callback(
                unreal._wellversed_tick_handle
            )
        except Exception:
            pass
        unreal._wellversed_tick_handle = None

    if unreal._wellversed_server is not None:
        try:
            unreal._wellversed_server.shutdown()
        except Exception:
            pass
        unreal._wellversed_server = None

    if unreal._wellversed_thread is not None:
        try:
            unreal._wellversed_thread.join(timeout=2.0)
        except Exception:
            pass
        unreal._wellversed_thread = None

    unreal._wellversed_port = None

    # Clear any pending items
    while not unreal._wellversed_queue.empty():
        try:
            unreal._wellversed_queue.get_nowait()
        except queue.Empty:
            break
    unreal._wellversed_responses.clear()

    unreal.log("[WellVersed] Bridge stopped")
