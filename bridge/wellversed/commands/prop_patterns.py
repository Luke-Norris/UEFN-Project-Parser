"""Procedural prop arrangement patterns -- circles, grids, lines, spirals.

Provides high-level placement commands that arrange actors in geometric
patterns. Each pattern spawns multiple actors from a single class path
and applies the requested spatial distribution.
"""

from __future__ import annotations

import math
import random

from ..listener import register_command
from ..safety import safe_transaction

try:
    import unreal
    from ..serialization import serialize_actor, deserialize_vector
except ImportError:
    unreal = None


# ---------------------------------------------------------------------------
# Internal helpers
# ---------------------------------------------------------------------------

def _find_mesh(candidates: list[str]):
    """Return the first mesh path that can be loaded."""
    for path in candidates:
        asset = unreal.EditorAssetLibrary.load_asset(path)
        if asset is not None:
            return path
    return None


def _load_actor_asset(class_path: str):
    """Load an asset or blueprint class for spawning."""
    asset = unreal.EditorAssetLibrary.load_asset(class_path)
    if asset is not None:
        return asset, "object"

    try:
        cls = getattr(unreal, class_path, None)
        if cls is not None:
            return cls, "class"
    except Exception:
        pass

    return None, None


def _spawn_from(asset, asset_type, location, rotation):
    """Spawn an actor from either an object or a class."""
    if asset_type == "object":
        return unreal.EditorLevelLibrary.spawn_actor_from_object(
            asset, location, rotation
        )
    elif asset_type == "class":
        return unreal.EditorLevelLibrary.spawn_actor_from_class(
            asset, location, rotation
        )
    return None


# ---------------------------------------------------------------------------
# Commands
# ---------------------------------------------------------------------------

@register_command("place_circle")
def place_circle(params: dict) -> dict:
    """Place actors in a circle pattern.

    Parameters
    ----------
    params.actor_class : str
        Asset or class path to spawn.
    params.center : dict
        ``{"x", "y", "z"}`` center of the circle.
    params.radius : float
        Circle radius in Unreal units.
    params.count : int
        Number of actors to place.
    params.face_center : bool, optional
        Rotate actors to face the center. Default True.
    params.z_offset : float, optional
        Vertical offset for each actor. Default 0.
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    actor_class = params["actor_class"]
    center = deserialize_vector(params.get("center", {}))
    radius = float(params["radius"])
    count = int(params["count"])
    face_center = params.get("face_center", True)
    z_offset = float(params.get("z_offset", 0))

    if count <= 0:
        return {"status": "error", "error": "count must be > 0"}

    asset, asset_type = _load_actor_asset(actor_class)
    if asset is None:
        return {"status": "error", "error": f"Could not load: {actor_class}"}

    spawned = []

    with safe_transaction(f"WellVersed: Place Circle ({count} actors)"):
        for i in range(count):
            angle = (2 * math.pi * i) / count
            x = center.x + radius * math.cos(angle)
            y = center.y + radius * math.sin(angle)
            z = center.z + z_offset

            loc = unreal.Vector(x=x, y=y, z=z)

            if face_center:
                yaw = math.degrees(math.atan2(
                    center.y - y, center.x - x
                ))
            else:
                yaw = 0

            rot = unreal.Rotator(pitch=0, yaw=yaw, roll=0)
            actor = _spawn_from(asset, asset_type, loc, rot)

            if actor is not None:
                actor.set_actor_label(f"WV_Circle_{i}")
                spawned.append(serialize_actor(actor))

    return {
        "status": "ok",
        "data": {
            "pattern": "circle",
            "placed": len(spawned),
            "requested": count,
            "actors": spawned,
        },
    }


@register_command("place_grid")
def place_grid(params: dict) -> dict:
    """Place actors in a rectangular grid pattern.

    Parameters
    ----------
    params.actor_class : str
        Asset or class path to spawn.
    params.origin : dict
        ``{"x", "y", "z"}`` corner of the grid.
    params.rows : int
        Number of rows (Y direction).
    params.cols : int
        Number of columns (X direction).
    params.spacing_x : float
        Distance between columns.
    params.spacing_y : float
        Distance between rows.
    params.z_offset : float, optional
        Vertical offset. Default 0.
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    actor_class = params["actor_class"]
    origin = deserialize_vector(params.get("origin", {}))
    rows = int(params.get("rows", 3))
    cols = int(params.get("cols", 3))
    spacing_x = float(params.get("spacing_x", 200))
    spacing_y = float(params.get("spacing_y", 200))
    z_offset = float(params.get("z_offset", 0))

    asset, asset_type = _load_actor_asset(actor_class)
    if asset is None:
        return {"status": "error", "error": f"Could not load: {actor_class}"}

    spawned = []
    total = rows * cols

    with safe_transaction(f"WellVersed: Place Grid ({total} actors)"):
        idx = 0
        for r in range(rows):
            for c in range(cols):
                x = origin.x + c * spacing_x
                y = origin.y + r * spacing_y
                z = origin.z + z_offset

                loc = unreal.Vector(x=x, y=y, z=z)
                rot = unreal.Rotator()
                actor = _spawn_from(asset, asset_type, loc, rot)

                if actor is not None:
                    actor.set_actor_label(f"WV_Grid_{r}_{c}")
                    spawned.append(serialize_actor(actor))
                idx += 1

    return {
        "status": "ok",
        "data": {
            "pattern": "grid",
            "placed": len(spawned),
            "requested": total,
            "rows": rows,
            "cols": cols,
            "actors": spawned,
        },
    }


@register_command("place_line")
def place_line(params: dict) -> dict:
    """Place actors evenly along a straight line.

    Parameters
    ----------
    params.actor_class : str
        Asset or class path to spawn.
    params.start : dict
        ``{"x", "y", "z"}`` start point.
    params.end : dict
        ``{"x", "y", "z"}`` end point.
    params.count : int
        Number of actors to place.
    params.align_to_line : bool, optional
        Rotate actors to face along the line direction. Default True.
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    actor_class = params["actor_class"]
    start = deserialize_vector(params["start"])
    end = deserialize_vector(params["end"])
    count = int(params["count"])
    align = params.get("align_to_line", True)

    if count <= 0:
        return {"status": "error", "error": "count must be > 0"}

    asset, asset_type = _load_actor_asset(actor_class)
    if asset is None:
        return {"status": "error", "error": f"Could not load: {actor_class}"}

    yaw = 0.0
    if align:
        dx = end.x - start.x
        dy = end.y - start.y
        yaw = math.degrees(math.atan2(dy, dx))

    spawned = []

    with safe_transaction(f"WellVersed: Place Line ({count} actors)"):
        for i in range(count):
            t = i / max(count - 1, 1)
            x = start.x + (end.x - start.x) * t
            y = start.y + (end.y - start.y) * t
            z = start.z + (end.z - start.z) * t

            loc = unreal.Vector(x=x, y=y, z=z)
            rot = unreal.Rotator(pitch=0, yaw=yaw if align else 0, roll=0)
            actor = _spawn_from(asset, asset_type, loc, rot)

            if actor is not None:
                actor.set_actor_label(f"WV_Line_{i}")
                spawned.append(serialize_actor(actor))

    return {
        "status": "ok",
        "data": {
            "pattern": "line",
            "placed": len(spawned),
            "requested": count,
            "actors": spawned,
        },
    }


@register_command("place_spiral")
def place_spiral(params: dict) -> dict:
    """Place actors in a spiral staircase pattern.

    Parameters
    ----------
    params.actor_class : str
        Asset or class path to spawn.
    params.center : dict
        ``{"x", "y", "z"}`` base center of the spiral.
    params.radius : float
        Spiral radius.
    params.height : float
        Total height of the spiral.
    params.turns : float
        Number of full rotations. Default 2.
    params.count : int
        Number of actors to place.
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    actor_class = params["actor_class"]
    center = deserialize_vector(params.get("center", {}))
    radius = float(params["radius"])
    height = float(params["height"])
    turns = float(params.get("turns", 2))
    count = int(params["count"])

    if count <= 0:
        return {"status": "error", "error": "count must be > 0"}

    asset, asset_type = _load_actor_asset(actor_class)
    if asset is None:
        return {"status": "error", "error": f"Could not load: {actor_class}"}

    spawned = []

    with safe_transaction(f"WellVersed: Place Spiral ({count} actors)"):
        for i in range(count):
            t = i / max(count - 1, 1)
            angle = 2 * math.pi * turns * t
            x = center.x + radius * math.cos(angle)
            y = center.y + radius * math.sin(angle)
            z = center.z + height * t

            loc = unreal.Vector(x=x, y=y, z=z)
            # Face outward from center
            yaw = math.degrees(math.atan2(y - center.y, x - center.x))
            rot = unreal.Rotator(pitch=0, yaw=yaw, roll=0)
            actor = _spawn_from(asset, asset_type, loc, rot)

            if actor is not None:
                actor.set_actor_label(f"WV_Spiral_{i}")
                spawned.append(serialize_actor(actor))

    return {
        "status": "ok",
        "data": {
            "pattern": "spiral",
            "placed": len(spawned),
            "requested": count,
            "radius": radius,
            "height": height,
            "turns": turns,
            "actors": spawned,
        },
    }


@register_command("place_random_in_area")
def place_random_in_area(params: dict) -> dict:
    """Scatter actors randomly within a rectangular area with minimum spacing.

    Uses rejection sampling to ensure no two actors are closer than
    min_spacing apart.

    Parameters
    ----------
    params.actor_class : str
        Asset or class path to spawn.
    params.center : dict
        ``{"x", "y", "z"}`` center of the area.
    params.width : float
        Area width (X dimension).
    params.depth : float
        Area depth (Y dimension).
    params.count : int
        Number of actors to place.
    params.min_spacing : float, optional
        Minimum distance between actors. Default 150.
    params.random_rotation : bool, optional
        Randomize yaw. Default True.
    params.seed : int, optional
        Random seed for reproducibility.
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    actor_class = params["actor_class"]
    center = deserialize_vector(params.get("center", {}))
    width = float(params["width"])
    depth = float(params["depth"])
    count = int(params["count"])
    min_spacing = float(params.get("min_spacing", 150))
    random_rotation = params.get("random_rotation", True)
    seed = params.get("seed")

    if count <= 0:
        return {"status": "error", "error": "count must be > 0"}

    asset, asset_type = _load_actor_asset(actor_class)
    if asset is None:
        return {"status": "error", "error": f"Could not load: {actor_class}"}

    rng = random.Random(seed)
    half_w = width / 2
    half_d = depth / 2

    # Rejection sampling for minimum spacing
    placed_positions: list[tuple[float, float]] = []
    max_attempts = count * 30

    for _ in range(max_attempts):
        if len(placed_positions) >= count:
            break

        x = rng.uniform(center.x - half_w, center.x + half_w)
        y = rng.uniform(center.y - half_d, center.y + half_d)

        too_close = False
        for px, py in placed_positions:
            if (x - px) ** 2 + (y - py) ** 2 < min_spacing ** 2:
                too_close = True
                break

        if not too_close:
            placed_positions.append((x, y))

    spawned = []

    with safe_transaction(f"WellVersed: Random Scatter ({len(placed_positions)} actors)"):
        for i, (x, y) in enumerate(placed_positions):
            loc = unreal.Vector(x=x, y=y, z=center.z)
            yaw = rng.uniform(0, 360) if random_rotation else 0
            rot = unreal.Rotator(pitch=0, yaw=yaw, roll=0)
            actor = _spawn_from(asset, asset_type, loc, rot)

            if actor is not None:
                actor.set_actor_label(f"WV_Random_{i}")
                spawned.append(serialize_actor(actor))

    return {
        "status": "ok",
        "data": {
            "pattern": "random_scatter",
            "placed": len(spawned),
            "requested": count,
            "actors": spawned,
        },
    }


@register_command("place_perimeter")
def place_perimeter(params: dict) -> dict:
    """Place actors along a rectangular perimeter (fences, walls).

    Parameters
    ----------
    params.actor_class : str
        Asset or class path to spawn.
    params.center : dict
        ``{"x", "y", "z"}`` center of the rectangle.
    params.width : float
        Rectangle width (X dimension).
    params.depth : float
        Rectangle depth (Y dimension).
    params.spacing : float
        Distance between actors along the perimeter.
    params.face_outward : bool, optional
        Rotate actors to face outward. Default True.
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    actor_class = params["actor_class"]
    center = deserialize_vector(params.get("center", {}))
    width = float(params["width"])
    depth = float(params["depth"])
    spacing = float(params["spacing"])
    face_outward = params.get("face_outward", True)

    if spacing <= 0:
        return {"status": "error", "error": "spacing must be > 0"}

    asset, asset_type = _load_actor_asset(actor_class)
    if asset is None:
        return {"status": "error", "error": f"Could not load: {actor_class}"}

    half_w = width / 2
    half_d = depth / 2

    # Generate perimeter points: walk around the rectangle
    perimeter_points = []
    perimeter_yaws = []

    # North edge (+Y): left to right
    count_x = max(1, int(width / spacing))
    for i in range(count_x):
        t = i / max(count_x, 1)
        x = center.x - half_w + width * t
        y = center.y + half_d
        perimeter_points.append((x, y))
        perimeter_yaws.append(0.0 if face_outward else 0.0)

    # East edge (+X): top to bottom
    count_y = max(1, int(depth / spacing))
    for i in range(count_y):
        t = i / max(count_y, 1)
        x = center.x + half_w
        y = center.y + half_d - depth * t
        perimeter_points.append((x, y))
        perimeter_yaws.append(90.0 if face_outward else 0.0)

    # South edge (-Y): right to left
    for i in range(count_x):
        t = i / max(count_x, 1)
        x = center.x + half_w - width * t
        y = center.y - half_d
        perimeter_points.append((x, y))
        perimeter_yaws.append(180.0 if face_outward else 0.0)

    # West edge (-X): bottom to top
    for i in range(count_y):
        t = i / max(count_y, 1)
        x = center.x - half_w
        y = center.y - half_d + depth * t
        perimeter_points.append((x, y))
        perimeter_yaws.append(270.0 if face_outward else 0.0)

    spawned = []

    with safe_transaction(f"WellVersed: Place Perimeter ({len(perimeter_points)} actors)"):
        for i, ((x, y), yaw) in enumerate(zip(perimeter_points, perimeter_yaws)):
            loc = unreal.Vector(x=x, y=y, z=center.z)
            rot = unreal.Rotator(pitch=0, yaw=yaw, roll=0)
            actor = _spawn_from(asset, asset_type, loc, rot)

            if actor is not None:
                actor.set_actor_label(f"WV_Perimeter_{i}")
                spawned.append(serialize_actor(actor))

    return {
        "status": "ok",
        "data": {
            "pattern": "perimeter",
            "placed": len(spawned),
            "perimeter_length": 2 * (width + depth),
            "actors": spawned,
        },
    }
