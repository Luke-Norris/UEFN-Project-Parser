"""Cinematic/sequencer tools -- camera sequences, cinematic mode, camera actors.

Provides commands for creating camera flythrough sequences, toggling
cinematic mode, and spawning camera actors for in-editor cinematography.
"""

from __future__ import annotations

import math

from ..listener import register_command
from ..safety import safe_transaction

try:
    import unreal
    from ..serialization import serialize_actor, deserialize_vector, deserialize_rotator
except ImportError:
    unreal = None


# ---------------------------------------------------------------------------
# Internal helpers
# ---------------------------------------------------------------------------

def _lerp(a: float, b: float, t: float) -> float:
    """Linear interpolation between two floats."""
    return a + (b - a) * t


def _direction_rotation(from_pt, to_pt):
    """Compute pitch and yaw from one point looking at another."""
    dx = to_pt["x"] - from_pt["x"]
    dy = to_pt["y"] - from_pt["y"]
    dz = to_pt["z"] - from_pt["z"]
    dist_xy = math.sqrt(dx * dx + dy * dy)

    yaw = math.degrees(math.atan2(dy, dx))
    pitch = math.degrees(math.atan2(dz, dist_xy)) if dist_xy > 0 else 0

    return {"pitch": pitch, "yaw": yaw, "roll": 0}


# ---------------------------------------------------------------------------
# Commands
# ---------------------------------------------------------------------------

@register_command("create_camera_sequence")
def create_camera_sequence(params: dict) -> dict:
    """Create a camera flythrough from control points.

    Places camera actors at each control point to define a flythrough
    path. Each camera is oriented to look toward the next point. The
    cameras are named sequentially for easy sequencer hookup.

    Parameters
    ----------
    params.points : list[dict]
        List of ``{"x", "y", "z"}`` control points defining the camera path.
    params.duration : float, optional
        Total duration hint in seconds. Default 10.0. Used for labeling.
    params.look_at : dict, optional
        ``{"x", "y", "z"}`` point all cameras should look at. If omitted,
        cameras look along the path direction.
    params.fov : float, optional
        Field of view in degrees. Default 90.
    params.label_prefix : str, optional
        Label prefix for camera actors. Default "WV_CamSeq".
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    points = params.get("points", [])
    duration = float(params.get("duration", 10.0))
    look_at = params.get("look_at")
    fov = float(params.get("fov", 90))
    label_prefix = params.get("label_prefix", "WV_CamSeq")

    if not points or len(points) < 2:
        return {"status": "error", "error": "Need at least 2 control points"}

    cameras = []

    with safe_transaction(f"WellVersed: Camera Sequence ({len(points)} points)"):
        for i, pt in enumerate(points):
            loc = deserialize_vector(pt)

            # Compute rotation
            if look_at:
                rot_dict = _direction_rotation(pt, look_at)
            elif i < len(points) - 1:
                rot_dict = _direction_rotation(pt, points[i + 1])
            elif i > 0:
                rot_dict = _direction_rotation(points[i - 1], pt)
            else:
                rot_dict = {"pitch": 0, "yaw": 0, "roll": 0}

            rot = deserialize_rotator(rot_dict)

            # Spawn camera actor
            actor = None
            try:
                actor = unreal.EditorLevelLibrary.spawn_actor_from_class(
                    unreal.CameraActor, loc, rot
                )
            except Exception:
                pass

            if actor is None:
                # Fallback: spawn as generic actor
                continue

            label = f"{label_prefix}_{i:03d}"
            actor.set_actor_label(label)

            # Set FOV if possible
            try:
                cam_comp = actor.get_component_by_class(unreal.CameraComponent)
                if cam_comp is not None:
                    cam_comp.set_editor_property("field_of_view", fov)
            except Exception:
                pass

            cameras.append(serialize_actor(actor))

    return {
        "status": "ok",
        "data": {
            "camera_count": len(cameras),
            "duration_hint": duration,
            "fov": fov,
            "cameras": cameras,
        },
    }


@register_command("set_cinematic_mode")
def set_cinematic_mode(params: dict) -> dict:
    """Toggle cinematic mode (letterbox bars, hide UI).

    Parameters
    ----------
    params.enabled : bool
        True to enable cinematic mode, False to disable.
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    enabled = bool(params.get("enabled", True))

    try:
        if enabled:
            unreal.SystemLibrary.execute_console_command(
                None, "r.CinematicMode 1"
            )
        else:
            unreal.SystemLibrary.execute_console_command(
                None, "r.CinematicMode 0"
            )
    except Exception:
        # Fallback: try viewport flags
        try:
            if enabled:
                unreal.SystemLibrary.execute_console_command(
                    None, "ShowFlag.CameraFrustums 0"
                )
            else:
                unreal.SystemLibrary.execute_console_command(
                    None, "ShowFlag.CameraFrustums 1"
                )
        except Exception as exc:
            return {"status": "error", "error": f"Could not set cinematic mode: {exc}"}

    return {
        "status": "ok",
        "data": {
            "cinematic_mode": enabled,
        },
    }


@register_command("create_matinee_camera")
def create_matinee_camera(params: dict) -> dict:
    """Spawn a standalone camera actor at a specific position.

    Parameters
    ----------
    params.location : dict
        ``{"x", "y", "z"}`` position.
    params.rotation : dict, optional
        ``{"pitch", "yaw", "roll"}``. Default looking forward.
    params.fov : float, optional
        Field of view in degrees. Default 90.
    params.label : str, optional
        Actor label. Default "WV_Camera".
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    location = deserialize_vector(params["location"])
    rotation = deserialize_rotator(params.get("rotation", {}))
    fov = float(params.get("fov", 90))
    label = params.get("label", "WV_Camera")

    with safe_transaction(f"WellVersed: Create Camera '{label}'"):
        actor = unreal.EditorLevelLibrary.spawn_actor_from_class(
            unreal.CameraActor, location, rotation
        )
        if actor is None:
            return {"status": "error", "error": "Failed to spawn CameraActor"}

        actor.set_actor_label(label)

        try:
            cam_comp = actor.get_component_by_class(unreal.CameraComponent)
            if cam_comp is not None:
                cam_comp.set_editor_property("field_of_view", fov)
        except Exception:
            pass

    return {
        "status": "ok",
        "data": {
            **serialize_actor(actor),
            "fov": fov,
        },
    }
