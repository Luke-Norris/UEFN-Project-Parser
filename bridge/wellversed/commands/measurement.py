"""Measurement tools -- distances, bounds, travel time, nearest device.

Provides commands for measuring spatial relationships between actors:
point-to-point distance, actor bounding boxes, level bounds, travel time
estimates, and nearest-device queries.
"""

from __future__ import annotations

import math

from ..listener import register_command

try:
    import unreal
    from ..serialization import serialize_actor, deserialize_vector
except ImportError:
    unreal = None


# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

# Default UEFN player movement speeds (Unreal units per second)
_PLAYER_SPEED_WALK = 600.0    # ~6 m/s walking
_PLAYER_SPEED_SPRINT = 900.0  # ~9 m/s sprinting


# ---------------------------------------------------------------------------
# Internal helpers
# ---------------------------------------------------------------------------

def _find_actor_by_name(name: str):
    """Find a level actor by its name or label."""
    for actor in unreal.EditorLevelLibrary.get_all_level_actors():
        if actor.get_name() == name or actor.get_actor_label() == name:
            return actor
    return None


def _distance_3d(a, b) -> float:
    dx = a.x - b.x
    dy = a.y - b.y
    dz = a.z - b.z
    return math.sqrt(dx * dx + dy * dy + dz * dz)


def _distance_2d(a, b) -> float:
    """Horizontal distance (ignoring Z)."""
    dx = a.x - b.x
    dy = a.y - b.y
    return math.sqrt(dx * dx + dy * dy)


# ---------------------------------------------------------------------------
# Commands
# ---------------------------------------------------------------------------

@register_command("measure_distance")
def measure_distance(params: dict) -> dict:
    """Measure distance between two actors.

    Parameters
    ----------
    params.actor_a : str
        Name or label of the first actor.
    params.actor_b : str
        Name or label of the second actor.
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    actor_a = _find_actor_by_name(params["actor_a"])
    if actor_a is None:
        return {"status": "error", "error": f"Actor not found: {params['actor_a']}"}

    actor_b = _find_actor_by_name(params["actor_b"])
    if actor_b is None:
        return {"status": "error", "error": f"Actor not found: {params['actor_b']}"}

    loc_a = actor_a.get_actor_location()
    loc_b = actor_b.get_actor_location()

    dist_3d = _distance_3d(loc_a, loc_b)
    dist_2d = _distance_2d(loc_a, loc_b)
    height_diff = loc_b.z - loc_a.z

    return {
        "status": "ok",
        "data": {
            "actor_a": params["actor_a"],
            "actor_b": params["actor_b"],
            "distance_3d": round(dist_3d, 2),
            "distance_2d": round(dist_2d, 2),
            "height_difference": round(height_diff, 2),
            "distance_meters": round(dist_3d / 100, 2),
            "location_a": {"x": loc_a.x, "y": loc_a.y, "z": loc_a.z},
            "location_b": {"x": loc_b.x, "y": loc_b.y, "z": loc_b.z},
        },
    }


@register_command("measure_bounds")
def measure_bounds(params: dict) -> dict:
    """Measure the bounding box dimensions of an actor.

    Uses the actor's components to compute the overall bounding box.

    Parameters
    ----------
    params.actor_name : str
        Actor name or label.
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    actor = _find_actor_by_name(params["actor_name"])
    if actor is None:
        return {"status": "error", "error": f"Actor not found: {params['actor_name']}"}

    loc = actor.get_actor_location()
    scale = actor.get_actor_scale3d()

    # Try to get bounds from the actor
    origin = unreal.Vector()
    extent = unreal.Vector()
    try:
        origin, extent = actor.get_actor_bounds(only_colliding_components=False)
    except Exception:
        # Fallback: estimate from scale (assumes 100-unit base)
        extent = unreal.Vector(
            x=abs(scale.x) * 50,
            y=abs(scale.y) * 50,
            z=abs(scale.z) * 50,
        )

    return {
        "status": "ok",
        "data": {
            "actor": params["actor_name"],
            "location": {"x": loc.x, "y": loc.y, "z": loc.z},
            "bounds_origin": {"x": origin.x, "y": origin.y, "z": origin.z},
            "bounds_extent": {"x": extent.x, "y": extent.y, "z": extent.z},
            "dimensions": {
                "width": round(extent.x * 2, 2),
                "depth": round(extent.y * 2, 2),
                "height": round(extent.z * 2, 2),
            },
            "dimensions_meters": {
                "width": round(extent.x * 2 / 100, 2),
                "depth": round(extent.y * 2 / 100, 2),
                "height": round(extent.z * 2 / 100, 2),
            },
        },
    }


@register_command("measure_level_bounds")
def measure_level_bounds(params: dict) -> dict:
    """Measure the overall level size from all actor positions.

    Parameters
    ----------
    (no parameters required)
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    all_actors = unreal.EditorLevelLibrary.get_all_level_actors()

    if not all_actors:
        return {"status": "ok", "data": {"actor_count": 0, "message": "No actors in level"}}

    min_x = min_y = min_z = float("inf")
    max_x = max_y = max_z = float("-inf")

    for actor in all_actors:
        loc = actor.get_actor_location()
        min_x = min(min_x, loc.x)
        min_y = min(min_y, loc.y)
        min_z = min(min_z, loc.z)
        max_x = max(max_x, loc.x)
        max_y = max(max_y, loc.y)
        max_z = max(max_z, loc.z)

    size_x = max_x - min_x
    size_y = max_y - min_y
    size_z = max_z - min_z

    return {
        "status": "ok",
        "data": {
            "actor_count": len(all_actors),
            "min": {"x": round(min_x, 2), "y": round(min_y, 2), "z": round(min_z, 2)},
            "max": {"x": round(max_x, 2), "y": round(max_y, 2), "z": round(max_z, 2)},
            "size": {"x": round(size_x, 2), "y": round(size_y, 2), "z": round(size_z, 2)},
            "size_meters": {
                "x": round(size_x / 100, 2),
                "y": round(size_y / 100, 2),
                "z": round(size_z / 100, 2),
            },
            "center": {
                "x": round((min_x + max_x) / 2, 2),
                "y": round((min_y + max_y) / 2, 2),
                "z": round((min_z + max_z) / 2, 2),
            },
        },
    }


@register_command("estimate_travel_time")
def estimate_travel_time(params: dict) -> dict:
    """Estimate how long it takes a player to walk/sprint between two points.

    Parameters
    ----------
    params.start : dict or str
        ``{"x", "y", "z"}`` start point, or actor name/label.
    params.end : dict or str
        ``{"x", "y", "z"}`` end point, or actor name/label.
    params.speed : float, optional
        Custom speed in UU/s. If omitted, computes for both walk and sprint.
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    # Resolve start
    start_data = params["start"]
    if isinstance(start_data, str):
        actor = _find_actor_by_name(start_data)
        if actor is None:
            return {"status": "error", "error": f"Actor not found: {start_data}"}
        loc = actor.get_actor_location()
        start_vec = unreal.Vector(x=loc.x, y=loc.y, z=loc.z)
    else:
        start_vec = deserialize_vector(start_data)

    # Resolve end
    end_data = params["end"]
    if isinstance(end_data, str):
        actor = _find_actor_by_name(end_data)
        if actor is None:
            return {"status": "error", "error": f"Actor not found: {end_data}"}
        loc = actor.get_actor_location()
        end_vec = unreal.Vector(x=loc.x, y=loc.y, z=loc.z)
    else:
        end_vec = deserialize_vector(end_data)

    dist = _distance_3d(start_vec, end_vec)
    dist_2d = _distance_2d(start_vec, end_vec)
    custom_speed = params.get("speed")

    result = {
        "distance_3d": round(dist, 2),
        "distance_2d": round(dist_2d, 2),
        "distance_meters": round(dist / 100, 2),
    }

    if custom_speed:
        speed = float(custom_speed)
        result["custom_speed"] = speed
        result["travel_time_seconds"] = round(dist / speed, 2) if speed > 0 else None
    else:
        result["walk_speed"] = _PLAYER_SPEED_WALK
        result["sprint_speed"] = _PLAYER_SPEED_SPRINT
        result["walk_time_seconds"] = round(dist / _PLAYER_SPEED_WALK, 2)
        result["sprint_time_seconds"] = round(dist / _PLAYER_SPEED_SPRINT, 2)

    return {
        "status": "ok",
        "data": result,
    }


@register_command("find_nearest_device")
def find_nearest_device(params: dict) -> dict:
    """Find the closest device of a given type to a location.

    Parameters
    ----------
    params.location : dict or str
        ``{"x", "y", "z"}`` reference point, or actor name/label.
    params.device_type : str, optional
        Device class substring to filter (e.g., "SpawnPad", "Vending").
        If omitted, searches all devices.
    params.count : int, optional
        Return the N nearest devices. Default 1.
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    # Resolve location
    loc_data = params["location"]
    if isinstance(loc_data, str):
        actor = _find_actor_by_name(loc_data)
        if actor is None:
            return {"status": "error", "error": f"Actor not found: {loc_data}"}
        loc = actor.get_actor_location()
        ref_point = unreal.Vector(x=loc.x, y=loc.y, z=loc.z)
    else:
        ref_point = deserialize_vector(loc_data)

    device_type = params.get("device_type", "").lower()
    count = int(params.get("count", 1))

    all_actors = unreal.EditorLevelLibrary.get_all_level_actors()

    # Filter to devices
    candidates = []
    device_keywords = ["device", "creative", "manager", "spawner", "trigger", "controller"]

    for actor in all_actors:
        class_name = actor.get_class().get_name().lower()

        is_device = any(kw in class_name for kw in device_keywords)
        if not is_device:
            continue

        if device_type and device_type not in class_name:
            continue

        actor_loc = actor.get_actor_location()
        dist = _distance_3d(ref_point, actor_loc)

        candidates.append((dist, actor))

    # Sort by distance
    candidates.sort(key=lambda x: x[0])

    results = []
    for dist, actor in candidates[:count]:
        data = serialize_actor(actor)
        data["distance"] = round(dist, 2)
        data["distance_meters"] = round(dist / 100, 2)
        results.append(data)

    return {
        "status": "ok",
        "data": {
            "count": len(results),
            "reference_point": {"x": ref_point.x, "y": ref_point.y, "z": ref_point.z},
            "nearest": results,
        },
    }
