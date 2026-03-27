"""WellVersed Bridge -- connects WellVersed desktop app to UEFN editor.

This package runs inside UEFN's embedded Python 3.11 interpreter. It starts
an HTTP listener on a background thread that accepts commands from the
WellVersed desktop application and dispatches them on the editor's main
thread via unreal.register_slate_post_tick_callback().

Usage from UEFN's Python console::

    import wellversed
    wellversed.start()   # start bridge listener
    wellversed.stop()    # stop bridge listener
"""

__version__ = "1.0.0"


def start():
    """Start the WellVersed bridge listener.

    Launches an HTTP server on a daemon thread and registers a tick callback
    to drain the command queue on the editor's main thread.
    """
    from .listener import start_listener
    start_listener()


def stop():
    """Stop the WellVersed bridge listener.

    Shuts down the HTTP server, unregisters the tick callback, and cleans up
    all shared state.
    """
    from .listener import stop_listener
    stop_listener()
