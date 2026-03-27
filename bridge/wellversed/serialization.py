"""Value serialization helpers for converting Unreal types to JSON-safe dicts.

UEFN's Python API exposes many Unreal types (``Vector``, ``Rotator``,
``LinearColor``, etc.) that are not directly JSON-serializable. This
module provides bidirectional conversion so command handlers can focus on
logic rather than type wrangling.
"""

from __future__ import annotations

from typing import Any

import unreal


# ---------------------------------------------------------------------------
# Unreal -> JSON
# ---------------------------------------------------------------------------

def serialize_value(val: Any) -> Any:
    """Convert an Unreal property value to a JSON-safe Python type.

    Handles common Unreal types: Vector, Rotator, Transform, LinearColor,
    SoftObjectPath, Name, Text, arrays, maps, and nested structs.
    Falls back to ``str(val)`` for unknown types.
    """
    if val is None:
        return None

    if isinstance(val, (bool, int, float, str)):
        return val

    if isinstance(val, unreal.Vector):
        return {"x": val.x, "y": val.y, "z": val.z}

    if isinstance(val, unreal.Rotator):
        return {"pitch": val.pitch, "yaw": val.yaw, "roll": val.roll}

    if isinstance(val, unreal.Transform):
        return {
            "location": serialize_value(val.translation),
            "rotation": serialize_value(val.rotation.rotator()),
            "scale": serialize_value(val.scale3d),
        }

    if isinstance(val, unreal.LinearColor):
        return {"r": val.r, "g": val.g, "b": val.b, "a": val.a}

    if isinstance(val, unreal.Color):
        return {"r": val.r, "g": val.g, "b": val.b, "a": val.a}

    if isinstance(val, unreal.Vector2D):
        return {"x": val.x, "y": val.y}

    if isinstance(val, (unreal.SoftObjectPath,)):
        return str(val)

    if isinstance(val, unreal.Name):
        return str(val)

    if isinstance(val, unreal.Text):
        return str(val)

    if isinstance(val, (list, unreal.Array)):
        return [serialize_value(item) for item in val]

    if isinstance(val, dict):
        return {str(k): serialize_value(v) for k, v in val.items()}

    # Enum values
    if hasattr(val, 'name') and hasattr(val, 'value'):
        try:
            return val.name
        except Exception:
            pass

    return str(val)


def serialize_actor(actor: unreal.Actor) -> dict:
    """Serialize an actor's essential data to a JSON-safe dict.

    Extracts name, class, transform components, folder path, tags,
    visibility, and mobility.
    """
    location = actor.get_actor_location()
    rotation = actor.get_actor_rotation()
    scale = actor.get_actor_scale3d()

    result = {
        "name": actor.get_name(),
        "label": actor.get_actor_label(),
        "class": actor.get_class().get_name(),
        "class_path": actor.get_class().get_path_name(),
        "location": {"x": location.x, "y": location.y, "z": location.z},
        "rotation": {
            "pitch": rotation.pitch,
            "yaw": rotation.yaw,
            "roll": rotation.roll,
        },
        "scale": {"x": scale.x, "y": scale.y, "z": scale.z},
    }

    # Optional metadata -- wrapped in try/except as not all actors support
    # every property.
    try:
        result["folder_path"] = actor.get_folder_path()
    except Exception:
        result["folder_path"] = ""

    try:
        result["tags"] = [str(t) for t in actor.tags]
    except Exception:
        result["tags"] = []

    try:
        result["hidden"] = actor.is_hidden_ed()
    except Exception:
        result["hidden"] = False

    return result


# ---------------------------------------------------------------------------
# JSON -> Unreal
# ---------------------------------------------------------------------------

def deserialize_vector(data: dict) -> unreal.Vector:
    """Convert ``{"x": ..., "y": ..., "z": ...}`` to ``unreal.Vector``."""
    return unreal.Vector(
        x=float(data.get("x", 0)),
        y=float(data.get("y", 0)),
        z=float(data.get("z", 0)),
    )


def deserialize_rotator(data: dict) -> unreal.Rotator:
    """Convert ``{"pitch": ..., "yaw": ..., "roll": ...}`` to ``unreal.Rotator``."""
    return unreal.Rotator(
        pitch=float(data.get("pitch", 0)),
        yaw=float(data.get("yaw", 0)),
        roll=float(data.get("roll", 0)),
    )


def coerce_property_value(value: Any, type_hint: str | None = None) -> Any:
    """Best-effort coercion of a JSON value to an Unreal-compatible type.

    Parameters
    ----------
    value:
        The raw value from JSON.
    type_hint:
        Optional hint like ``"Vector"``, ``"Rotator"``, ``"LinearColor"``,
        ``"bool"``, ``"int"``, ``"float"``.

    Returns
    -------
    Any
        The coerced value, or the original value if no coercion applies.
    """
    if type_hint == "Vector" and isinstance(value, dict):
        return deserialize_vector(value)
    if type_hint == "Rotator" and isinstance(value, dict):
        return deserialize_rotator(value)
    if type_hint == "LinearColor" and isinstance(value, dict):
        return unreal.LinearColor(
            r=float(value.get("r", 0)),
            g=float(value.get("g", 0)),
            b=float(value.get("b", 0)),
            a=float(value.get("a", 1)),
        )
    if type_hint == "bool":
        return bool(value)
    if type_hint == "int":
        return int(value)
    if type_hint == "float":
        return float(value)

    # Auto-detect dict shapes
    if isinstance(value, dict):
        keys = set(value.keys())
        if keys == {"x", "y", "z"}:
            return deserialize_vector(value)
        if keys == {"pitch", "yaw", "roll"}:
            return deserialize_rotator(value)
        if keys <= {"r", "g", "b", "a"} and len(keys) >= 3:
            return unreal.LinearColor(
                r=float(value.get("r", 0)),
                g=float(value.get("g", 0)),
                b=float(value.get("b", 0)),
                a=float(value.get("a", 1)),
            )

    return value
