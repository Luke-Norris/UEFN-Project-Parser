"""Verse deployment and build integration.

Provides commands to write Verse source files into the project's Verse
directory and trigger / monitor compilation via the editor.
"""

from __future__ import annotations

import os
import re
import time
from pathlib import Path

import unreal

from ..listener import register_command
from ..safety import safe_transaction


# ---------------------------------------------------------------------------
# Internal helpers
# ---------------------------------------------------------------------------

def _verse_dir() -> Path:
    """Locate the project's Verse source directory.

    UEFN projects store Verse files under ``Content/*.verse`` or in
    plugin directories like ``Plugins/<Name>/Content/*.verse``.
    """
    project_dir = Path(unreal.Paths.project_dir())

    # Check for a Plugins directory with verse files first
    plugins_dir = project_dir / "Plugins"
    if plugins_dir.is_dir():
        for plugin in plugins_dir.iterdir():
            content_dir = plugin / "Content"
            if content_dir.is_dir():
                verse_files = list(content_dir.rglob("*.verse"))
                if verse_files:
                    return content_dir

    # Fall back to Content directory
    content_dir = project_dir / "Content"
    if content_dir.is_dir():
        return content_dir

    return project_dir / "Content"


def _logs_dir() -> Path:
    """Locate the Saved/Logs directory."""
    return Path(unreal.Paths.project_saved_dir()) / "Logs"


def _find_latest_log() -> Path | None:
    """Find the most recent log file."""
    logs_dir = _logs_dir()
    if not logs_dir.is_dir():
        return None

    log_files = sorted(
        logs_dir.glob("*.log"),
        key=lambda p: p.stat().st_mtime,
        reverse=True,
    )
    return log_files[0] if log_files else None


def _parse_build_errors(log_text: str) -> list[dict]:
    """Extract structured build errors from a log file's contents."""
    errors = []
    # Common Verse/UEFN error patterns:
    #   file.verse(10:5): error V1234: Description
    #   file.verse(10): error: Description
    error_pattern = re.compile(
        r"([^\s(]+\.verse)"           # filename
        r"\((\d+)(?::(\d+))?\)"       # (line[:col])
        r":\s*(error|warning)"        # severity
        r"(?:\s+[A-Z]\d+)?:\s*"      # optional error code
        r"(.+)"                       # message
    )

    for line in log_text.splitlines():
        m = error_pattern.search(line)
        if m:
            errors.append({
                "file": m.group(1),
                "line": int(m.group(2)),
                "column": int(m.group(3)) if m.group(3) else None,
                "severity": m.group(4),
                "message": m.group(5).strip(),
            })

    return errors


# ---------------------------------------------------------------------------
# Commands
# ---------------------------------------------------------------------------

@register_command("write_verse_file")
def write_verse_file(params: dict) -> dict:
    """Write Verse source code to a file in the project.

    Parameters
    ----------
    params.filename : str
        File name (e.g. ``"my_device.verse"``). Must end with ``.verse``.
    params.content : str
        The Verse source code to write.
    params.subdirectory : str, optional
        Subdirectory within the Verse content folder.
    params.overwrite : bool, optional
        Allow overwriting an existing file. Default False.
    """
    filename = params["filename"]
    content = params["content"]
    subdirectory = params.get("subdirectory", "")
    overwrite = params.get("overwrite", False)

    if not filename.endswith(".verse"):
        raise ValueError("Filename must end with .verse")

    target_dir = _verse_dir()
    if subdirectory:
        target_dir = target_dir / subdirectory
    target_dir.mkdir(parents=True, exist_ok=True)

    file_path = target_dir / filename
    if file_path.exists() and not overwrite:
        raise ValueError(
            f"File already exists: {file_path}. Set overwrite=true to replace."
        )

    with safe_transaction(f"WellVersed: Write Verse '{filename}'"):
        with open(file_path, "w", encoding="utf-8") as f:
            f.write(content)

    unreal.log(f"[WellVersed] Verse file written: {file_path}")
    return {
        "file": str(file_path),
        "size": len(content),
        "lines": content.count("\n") + 1,
    }


@register_command("trigger_build")
def trigger_build(params: dict) -> dict:
    """Trigger a Verse compilation / project build.

    Attempts to trigger compilation by calling editor utilities. The exact
    mechanism depends on UEFN's available Python API. This tries multiple
    approaches:

    1. ``unreal.EditorLevelLibrary.editor_play_simulate()`` to force compile
    2. Direct compile command if available
    """
    method = "unknown"

    try:
        # Try PIE simulation which forces Verse compilation
        unreal.EditorLevelLibrary.editor_play_simulate()
        method = "play_simulate"
        # Stop simulation after a brief moment to just get the compile
        time.sleep(0.5)
        try:
            unreal.EditorLevelLibrary.editor_end_play()
        except Exception:
            pass
    except Exception as exc:
        unreal.log_warning(
            f"[WellVersed] play_simulate not available: {exc}"
        )
        # Try console command approach
        try:
            unreal.SystemLibrary.execute_console_command(
                None, "CompileVerseCode"
            )
            method = "console_command"
        except Exception:
            return {
                "triggered": False,
                "error": "No build trigger method available",
            }

    unreal.log("[WellVersed] Build triggered")
    return {"triggered": True, "method": method}


@register_command("get_build_status")
def get_build_status(params: dict) -> dict:
    """Read and summarize the latest build log.

    Returns the tail of the log file and whether the build appears to have
    succeeded or failed.
    """
    log_file = _find_latest_log()
    if log_file is None:
        return {"status": "unknown", "error": "No log file found"}

    try:
        with open(log_file, "r", encoding="utf-8", errors="replace") as f:
            text = f.read()
    except Exception as exc:
        return {"status": "unknown", "error": str(exc)}

    # Take the last 200 lines
    lines = text.splitlines()
    tail = lines[-200:] if len(lines) > 200 else lines

    # Look for success / failure indicators
    tail_text = "\n".join(tail)
    has_errors = bool(re.search(r"error\b", tail_text, re.IGNORECASE))
    has_success = bool(
        re.search(r"(build succeeded|compile succeeded|cook succeeded)",
                  tail_text, re.IGNORECASE)
    )

    if has_success and not has_errors:
        status = "success"
    elif has_errors:
        status = "failed"
    else:
        status = "unknown"

    return {
        "status": status,
        "log_file": str(log_file),
        "tail": tail_text,
        "line_count": len(lines),
    }


@register_command("list_verse_files")
def list_verse_files(params: dict) -> dict:
    """List all .verse files in the project.

    Scans the project's content directories for Verse source files.

    Parameters
    ----------
    params.subdirectory : str, optional
        Only search within this subdirectory.
    """
    verse_dir = _verse_dir()
    subdirectory = params.get("subdirectory", "")

    if subdirectory:
        search_dir = verse_dir / subdirectory
    else:
        search_dir = verse_dir

    if not search_dir.is_dir():
        return {"files": [], "count": 0, "directory": str(search_dir)}

    files = []
    for verse_file in sorted(search_dir.rglob("*.verse")):
        try:
            stat = verse_file.stat()
            with open(verse_file, "r", encoding="utf-8", errors="replace") as f:
                content = f.read()
            files.append({
                "name": verse_file.name,
                "path": str(verse_file),
                "relative_path": str(verse_file.relative_to(verse_dir)),
                "size": stat.st_size,
                "lines": content.count("\n") + 1,
            })
        except Exception:
            files.append({
                "name": verse_file.name,
                "path": str(verse_file),
                "error": "Could not read file",
            })

    return {
        "count": len(files),
        "directory": str(verse_dir),
        "files": files,
    }


@register_command("read_verse_file")
def read_verse_file(params: dict) -> dict:
    """Read the content of a specific Verse file.

    Parameters
    ----------
    params.filename : str
        File name (e.g., ``"my_device.verse"``) or relative path.
    """
    filename = params["filename"]
    verse_dir = _verse_dir()

    # Try direct path first
    file_path = verse_dir / filename
    if not file_path.exists():
        # Search recursively
        matches = list(verse_dir.rglob(filename))
        if matches:
            file_path = matches[0]
        else:
            raise ValueError(f"Verse file not found: {filename}")

    with open(file_path, "r", encoding="utf-8", errors="replace") as f:
        content = f.read()

    # Basic analysis
    lines = content.splitlines()
    imports = [l.strip() for l in lines if l.strip().startswith("using")]
    classes = [
        l.strip() for l in lines
        if any(kw in l for kw in [" := class", " := struct", " := interface"])
    ]

    return {
        "file": str(file_path),
        "name": file_path.name,
        "content": content,
        "lines": len(lines),
        "size": len(content),
        "imports": imports,
        "class_definitions": classes,
    }


@register_command("check_verse_syntax")
def check_verse_syntax(params: dict) -> dict:
    """Basic syntax validation for Verse source code.

    Performs lightweight checks without full parsing: indentation
    consistency, import format, bracket balance, and common issues.
    Not a substitute for the Verse compiler.

    Parameters
    ----------
    params.content : str
        Verse source code to check.
    """
    content = params["content"]
    lines = content.splitlines()
    issues = []

    # Check 1: Indentation consistency
    indent_chars = set()
    for i, line in enumerate(lines, 1):
        if line and not line.strip():
            continue  # skip blank lines
        stripped = line.lstrip()
        if stripped and line != stripped:
            indent = line[:len(line) - len(stripped)]
            if "\t" in indent:
                indent_chars.add("tab")
            if " " in indent:
                indent_chars.add("space")

    if len(indent_chars) > 1:
        issues.append({
            "severity": "warning",
            "message": "Mixed tabs and spaces in indentation",
            "line": None,
        })

    # Check 2: Import format
    for i, line in enumerate(lines, 1):
        stripped = line.strip()
        if stripped.startswith("using") and not stripped.startswith("using {"):
            if "{" not in stripped:
                issues.append({
                    "severity": "warning",
                    "message": f"Import may be missing braces: {stripped}",
                    "line": i,
                })

    # Check 3: Bracket/brace balance
    open_braces = 0
    open_parens = 0
    open_brackets = 0
    for i, line in enumerate(lines, 1):
        # Skip comments
        stripped = line.strip()
        if stripped.startswith("#"):
            continue
        open_braces += line.count("{") - line.count("}")
        open_parens += line.count("(") - line.count(")")
        open_brackets += line.count("[") - line.count("]")

    if open_braces != 0:
        issues.append({
            "severity": "error",
            "message": f"Unbalanced braces: {'+' if open_braces > 0 else ''}{open_braces} unclosed",
            "line": None,
        })
    if open_parens != 0:
        issues.append({
            "severity": "error",
            "message": f"Unbalanced parentheses: {'+' if open_parens > 0 else ''}{open_parens} unclosed",
            "line": None,
        })
    if open_brackets != 0:
        issues.append({
            "severity": "error",
            "message": f"Unbalanced brackets: {'+' if open_brackets > 0 else ''}{open_brackets} unclosed",
            "line": None,
        })

    # Check 4: Common mistakes
    for i, line in enumerate(lines, 1):
        stripped = line.strip()
        # Semicolons (Verse doesn't use them)
        if stripped.endswith(";") and not stripped.startswith("#"):
            issues.append({
                "severity": "warning",
                "message": "Verse doesn't use semicolons",
                "line": i,
            })
        # Common typo: = instead of :=
        if " = " in stripped and " := " not in stripped and not stripped.startswith("#"):
            if "==" not in stripped and "!=" not in stripped and "<=" not in stripped and ">=" not in stripped:
                issues.append({
                    "severity": "warning",
                    "message": "Possible assignment should use ':=' instead of '='",
                    "line": i,
                })

    error_count = sum(1 for i in issues if i["severity"] == "error")
    warning_count = sum(1 for i in issues if i["severity"] == "warning")

    return {
        "valid": error_count == 0,
        "error_count": error_count,
        "warning_count": warning_count,
        "issues": issues,
        "line_count": len(lines),
    }


@register_command("get_build_errors")
def get_build_errors(params: dict) -> dict:
    """Extract structured Verse build errors from the latest log.

    Returns
    -------
    dict
        ``{"errors": [...], "warnings": [...]}`` with file, line, column,
        and message for each diagnostic.
    """
    log_file = _find_latest_log()
    if log_file is None:
        return {"errors": [], "warnings": [], "error": "No log file found"}

    try:
        with open(log_file, "r", encoding="utf-8", errors="replace") as f:
            text = f.read()
    except Exception as exc:
        return {"errors": [], "warnings": [], "error": str(exc)}

    diagnostics = _parse_build_errors(text)
    errors = [d for d in diagnostics if d["severity"] == "error"]
    warnings = [d for d in diagnostics if d["severity"] == "warning"]

    return {
        "error_count": len(errors),
        "warning_count": len(warnings),
        "errors": errors,
        "warnings": warnings,
        "log_file": str(log_file),
    }
