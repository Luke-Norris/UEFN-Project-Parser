"""WellVersed Bridge auto-start for UEFN.

Place this file in your UEFN project's Content/Python/ directory.
It will automatically start the WellVersed bridge when UEFN opens
the project.

To install, copy this file to:
    <YourProject>/Plugins/<YourPlugin>/Content/Python/init_unreal.py

The bridge listens on http://127.0.0.1:9220 (or 9221/9222 if taken)
and accepts commands from the WellVersed desktop application.

To stop the bridge, run in UEFN's Python console:
    import wellversed; wellversed.stop()
"""

try:
    import wellversed
    wellversed.start()
    print("[WellVersed] Bridge started successfully")
except Exception as e:
    print(f"[WellVersed] Failed to start bridge: {e}")
