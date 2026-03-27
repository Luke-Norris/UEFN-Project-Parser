"""Viewport and camera control.

Provides commands to read/set the editor viewport camera position,
focus on specific actors, and capture viewport screenshots.
"""

from __future__ import annotations

import time
from pathlib import Path

import unreal

from ..listener import register_command
from ..serialization import deserialize_vector, deserialize_rotator


# ---------------------------------------------------------------------------
# Internal helpers
# ---------------------------------------------------------------------------

def _get_level_viewport():
    """Get the active level viewport client."""
    try:
        subsystem = unreal.get_editor_subsystem(
            unreal.UnrealEditorSubsystem
        )
        return subsystem
    except Exception:
        return None


def _find_actor_by_name(name: str) -> unreal.Actor | None:
    for actor in unreal.EditorLevelLibrary.get_all_level_actors():
        if actor.get_name() == name or actor.get_actor_label() == name:
            return actor
    return None


def _screenshots_dir() -> Path:
    """Return the screenshots directory, creating it if necessary."""
    project_dir = Path(unreal.Paths.project_dir())
    screenshots = project_dir / "Saved" / "WellVersed" / "screenshots"
    screenshots.mkdir(parents=True, exist_ok=True)
    return screenshots


# ---------------------------------------------------------------------------
# Commands
# ---------------------------------------------------------------------------

@register_command("get_viewport_camera")
def get_viewport_camera(params: dict) -> dict:
    """Read the current viewport camera position and rotation.

    Returns
    -------
    dict
        ``{"location": {...}, "rotation": {...}, "fov": float}``.
    """
    try:
        # Try EditorActorSubsystem approach
        subsystem = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
        loc, rot = subsystem.get_level_viewport_camera_info()
        return {
            "location": {"x": loc.x, "y": loc.y, "z": loc.z},
            "rotation": {
                "pitch": rot.pitch,
                "yaw": rot.yaw,
                "roll": rot.roll,
            },
        }
    except Exception:
        pass

    # Fallback: read from editor utility
    try:
        loc = unreal.EditorLevelLibrary.get_level_viewport_camera_info()
        if isinstance(loc, tuple) and len(loc) >= 2:
            position, rotation = loc[0], loc[1]
            return {
                "location": {
                    "x": position.x,
                    "y": position.y,
                    "z": position.z,
                },
                "rotation": {
                    "pitch": rotation.pitch,
                    "yaw": rotation.yaw,
                    "roll": rotation.roll,
                },
            }
    except Exception:
        pass

    return {"error": "Could not read viewport camera"}


@register_command("set_viewport_camera")
def set_viewport_camera(params: dict) -> dict:
    """Set the viewport camera position and rotation.

    Parameters
    ----------
    params.location : dict
        ``{"x", "y", "z"}``.
    params.rotation : dict, optional
        ``{"pitch", "yaw", "roll"}``.
    """
    location = deserialize_vector(params["location"])
    rotation = deserialize_rotator(params.get("rotation", {}))

    try:
        subsystem = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
        subsystem.set_level_viewport_camera_info(location, rotation)
        return {
            "location": {"x": location.x, "y": location.y, "z": location.z},
            "rotation": {
                "pitch": rotation.pitch,
                "yaw": rotation.yaw,
                "roll": rotation.roll,
            },
        }
    except Exception:
        pass

    # Fallback
    try:
        unreal.EditorLevelLibrary.set_level_viewport_camera_info(
            location, rotation
        )
        return {
            "location": {"x": location.x, "y": location.y, "z": location.z},
            "rotation": {
                "pitch": rotation.pitch,
                "yaw": rotation.yaw,
                "roll": rotation.roll,
            },
        }
    except Exception as exc:
        raise RuntimeError(f"Could not set viewport camera: {exc}")


@register_command("focus_on_actor")
def focus_on_actor(params: dict) -> dict:
    """Focus the viewport camera on a specific actor.

    Parameters
    ----------
    params.name : str
        Actor name or label.
    params.distance : float, optional
        Camera distance from actor. Default 500.
    params.pitch : float, optional
        Camera pitch angle in degrees. Default -30.
    """
    actor = _find_actor_by_name(params["name"])
    if actor is None:
        raise ValueError(f"Actor not found: {params['name']}")

    distance = float(params.get("distance", 500))
    pitch = float(params.get("pitch", -30))

    # Select and focus
    try:
        unreal.EditorLevelLibrary.set_selected_level_actors([actor])
    except Exception:
        pass

    # Position camera behind and above the actor
    actor_loc = actor.get_actor_location()
    import math
    rad_pitch = math.radians(pitch)

    cam_loc = unreal.Vector(
        x=actor_loc.x - distance * math.cos(rad_pitch),
        y=actor_loc.y,
        z=actor_loc.z - distance * math.sin(rad_pitch),
    )
    cam_rot = unreal.Rotator(pitch=pitch, yaw=0, roll=0)

    try:
        subsystem = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
        subsystem.set_level_viewport_camera_info(cam_loc, cam_rot)
    except Exception:
        try:
            unreal.EditorLevelLibrary.set_level_viewport_camera_info(
                cam_loc, cam_rot
            )
        except Exception as exc:
            raise RuntimeError(f"Could not focus camera: {exc}")

    return {
        "focused_on": actor.get_name(),
        "camera_location": {"x": cam_loc.x, "y": cam_loc.y, "z": cam_loc.z},
        "camera_rotation": {
            "pitch": cam_rot.pitch,
            "yaw": cam_rot.yaw,
            "roll": cam_rot.roll,
        },
    }


@register_command("take_screenshot")
def take_screenshot(params: dict) -> dict:
    """Capture the viewport to a file.

    Parameters
    ----------
    params.filename : str, optional
        Output filename. Default auto-generated with timestamp.
    params.resolution_x : int, optional
        Capture width. Default 1920.
    params.resolution_y : int, optional
        Capture height. Default 1080.

    Returns
    -------
    dict
        ``{"file": str, "resolution": {...}}``.
    """
    resolution_x = int(params.get("resolution_x", 1920))
    resolution_y = int(params.get("resolution_y", 1080))

    filename = params.get("filename")
    if not filename:
        timestamp = time.strftime("%Y%m%d_%H%M%S")
        filename = f"wellversed_{timestamp}.png"

    if not filename.endswith(".png"):
        filename += ".png"

    output_path = _screenshots_dir() / filename

    try:
        # Use high-resolution screenshot capture
        unreal.AutomationLibrary.take_high_res_screenshot(
            resolution_x, resolution_y, str(output_path)
        )
    except Exception:
        # Fallback: try console command
        try:
            unreal.SystemLibrary.execute_console_command(
                None,
                f"HighResShot {resolution_x}x{resolution_y} "
                f"Filename={output_path}",
            )
        except Exception as exc:
            raise RuntimeError(f"Screenshot capture failed: {exc}")

    return {
        "file": str(output_path),
        "resolution": {"x": resolution_x, "y": resolution_y},
    }
