"""Foliage management -- Poisson-disk scattering, density maps, transform randomization.

Dedicated foliage module with higher-fidelity placement algorithms than
the basic ``scatter_foliage`` in environment.py. Supports Poisson-disk
sampling, variable-density placement, area clearing, and transform
randomization for existing foliage.
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

def _poisson_disk_2d(
    min_x: float, max_x: float,
    min_y: float, max_y: float,
    spacing: float,
    max_points: int = 1000,
    seed: int | None = None,
) -> list[tuple[float, float]]:
    """Bridger's algorithm for Poisson-disk sampling in 2D.

    Uses active-list dart throwing with a background grid for O(1) neighbor
    checks. More natural distribution than uniform random.
    """
    rng = random.Random(seed)
    cell_size = spacing / math.sqrt(2)
    cols = max(1, int(math.ceil((max_x - min_x) / cell_size)))
    rows = max(1, int(math.ceil((max_y - min_y) / cell_size)))

    grid: list[list[tuple[float, float] | None]] = [
        [None] * cols for _ in range(rows)
    ]
    points: list[tuple[float, float]] = []

    def _gc(x: float, y: float) -> tuple[int, int]:
        col = int((x - min_x) / cell_size)
        row = int((y - min_y) / cell_size)
        return max(0, min(col, cols - 1)), max(0, min(row, rows - 1))

    def _valid(x: float, y: float) -> bool:
        gc, gr = _gc(x, y)
        for dr in range(-2, 3):
            for dc in range(-2, 3):
                nr, nc = gr + dr, gc + dc
                if 0 <= nr < rows and 0 <= nc < cols:
                    nb = grid[nr][nc]
                    if nb is not None:
                        dx = nb[0] - x
                        dy = nb[1] - y
                        if dx * dx + dy * dy < spacing * spacing:
                            return False
        return True

    # Seed point
    sx = rng.uniform(min_x, max_x)
    sy = rng.uniform(min_y, max_y)
    points.append((sx, sy))
    gc, gr = _gc(sx, sy)
    grid[gr][gc] = (sx, sy)

    active = [0]
    attempts = 30

    while active and len(points) < max_points:
        idx = rng.randint(0, len(active) - 1)
        px, py = points[active[idx]]

        found = False
        for _ in range(attempts):
            angle = rng.uniform(0, 2 * math.pi)
            dist = rng.uniform(spacing, 2 * spacing)
            nx = px + dist * math.cos(angle)
            ny = py + dist * math.sin(angle)

            if min_x <= nx <= max_x and min_y <= ny <= max_y and _valid(nx, ny):
                gc, gr = _gc(nx, ny)
                grid[gr][gc] = (nx, ny)
                points.append((nx, ny))
                active.append(len(points) - 1)
                found = True
                break

        if not found:
            active.pop(idx)

    return points


def _find_actors_of_class(class_name: str) -> list:
    """Find all actors whose class name matches (case-insensitive contains)."""
    results = []
    for actor in unreal.EditorLevelLibrary.get_all_level_actors():
        if class_name.lower() in actor.get_class().get_name().lower():
            results.append(actor)
    return results


def _in_area(loc, center, half_w, half_d) -> bool:
    """Check if a location is within a rectangular area."""
    return (
        abs(loc.x - center.x) <= half_w
        and abs(loc.y - center.y) <= half_d
    )


# ---------------------------------------------------------------------------
# Commands
# ---------------------------------------------------------------------------

@register_command("scatter_foliage_poisson")
def scatter_foliage_poisson(params: dict) -> dict:
    """Scatter foliage using Poisson-disk sampling for natural distribution.

    Poisson-disk sampling ensures a minimum distance between all instances,
    producing a more natural look than uniform random placement.

    Parameters
    ----------
    params.mesh_path : str
        Asset path to the foliage mesh or blueprint.
    params.area_center : dict
        ``{"x", "y", "z"}`` center of the scatter area.
    params.area_size : dict
        ``{"x", "y"}`` width and depth of the scatter area.
    params.min_distance : float
        Minimum distance between foliage instances.
    params.max_count : int, optional
        Maximum number of instances. Default 200.
    params.random_scale : dict, optional
        ``{"min": 0.7, "max": 1.3}`` scale range.
    params.random_rotation : bool, optional
        Randomize yaw. Default True.
    params.seed : int, optional
        Random seed for reproducibility.
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    mesh_path = params["mesh_path"]
    center = deserialize_vector(params["area_center"])
    area_size = params.get("area_size", {"x": 2000, "y": 2000})
    min_distance = float(params["min_distance"])
    max_count = int(params.get("max_count", 200))
    scale_range = params.get("random_scale", {"min": 0.8, "max": 1.2})
    random_rotation = params.get("random_rotation", True)
    seed = params.get("seed")

    asset = unreal.EditorAssetLibrary.load_asset(mesh_path)
    if asset is None:
        return {"status": "error", "error": f"Could not load: {mesh_path}"}

    half_x = float(area_size.get("x", 2000)) / 2
    half_y = float(area_size.get("y", 2000)) / 2

    points = _poisson_disk_2d(
        center.x - half_x, center.x + half_x,
        center.y - half_y, center.y + half_y,
        min_distance,
        max_points=max_count,
        seed=seed,
    )

    rng = random.Random(seed)
    spawned = []

    with safe_transaction(f"WellVersed: Poisson Foliage ({len(points)} items)"):
        for i, (px, py) in enumerate(points):
            loc = unreal.Vector(x=px, y=py, z=center.z)
            yaw = rng.uniform(0, 360) if random_rotation else 0
            rot = unreal.Rotator(pitch=0, yaw=yaw, roll=0)

            actor = unreal.EditorLevelLibrary.spawn_actor_from_object(
                asset, loc, rot
            )
            if actor is None:
                continue

            s_min = float(scale_range.get("min", 0.8))
            s_max = float(scale_range.get("max", 1.2))
            s = rng.uniform(s_min, s_max)
            actor.set_actor_scale3d(unreal.Vector(x=s, y=s, z=s))
            actor.set_actor_label(f"WV_Foliage_{i}")

            spawned.append(serialize_actor(actor))

    return {
        "status": "ok",
        "data": {
            "placed": len(spawned),
            "max_count": max_count,
            "min_distance": min_distance,
            "actors": spawned,
        },
    }


@register_command("scatter_foliage_density_map")
def scatter_foliage_density_map(params: dict) -> dict:
    """Scatter foliage with variable density across the area.

    Accepts a density function specification that varies the placement
    density across the area. Supports predefined patterns: "center_heavy",
    "edge_heavy", "gradient_x", "gradient_y", "uniform".

    Parameters
    ----------
    params.mesh_path : str
        Asset path to the foliage mesh or blueprint.
    params.area_center : dict
        ``{"x", "y", "z"}`` center of the scatter area.
    params.area_size : dict
        ``{"x", "y"}`` width and depth.
    params.density_pattern : str
        One of: "center_heavy", "edge_heavy", "gradient_x",
        "gradient_y", "uniform". Default "uniform".
    params.base_count : int
        Base number of instances. Default 100.
    params.random_scale : dict, optional
        ``{"min": 0.7, "max": 1.3}`` scale range.
    params.seed : int, optional
        Random seed.
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    mesh_path = params["mesh_path"]
    center = deserialize_vector(params["area_center"])
    area_size = params.get("area_size", {"x": 2000, "y": 2000})
    pattern = params.get("density_pattern", "uniform")
    base_count = int(params.get("base_count", 100))
    scale_range = params.get("random_scale", {"min": 0.8, "max": 1.2})
    seed = params.get("seed")

    asset = unreal.EditorAssetLibrary.load_asset(mesh_path)
    if asset is None:
        return {"status": "error", "error": f"Could not load: {mesh_path}"}

    rng = random.Random(seed)
    half_x = float(area_size.get("x", 2000)) / 2
    half_y = float(area_size.get("y", 2000)) / 2

    # Generate candidate points with rejection based on density
    placed = []
    attempts = base_count * 5

    for _ in range(attempts):
        if len(placed) >= base_count:
            break

        x = rng.uniform(center.x - half_x, center.x + half_x)
        y = rng.uniform(center.y - half_y, center.y + half_y)

        # Compute density at this point (0-1)
        norm_x = (x - (center.x - half_x)) / (2 * half_x) if half_x > 0 else 0.5
        norm_y = (y - (center.y - half_y)) / (2 * half_y) if half_y > 0 else 0.5

        if pattern == "center_heavy":
            dist = math.sqrt((norm_x - 0.5) ** 2 + (norm_y - 0.5) ** 2) / 0.707
            density = max(0, 1 - dist)
        elif pattern == "edge_heavy":
            dist = math.sqrt((norm_x - 0.5) ** 2 + (norm_y - 0.5) ** 2) / 0.707
            density = dist
        elif pattern == "gradient_x":
            density = norm_x
        elif pattern == "gradient_y":
            density = norm_y
        else:
            density = 1.0

        if rng.random() < density:
            placed.append((x, y))

    spawned = []

    with safe_transaction(f"WellVersed: Density Foliage ({len(placed)} items)"):
        for i, (x, y) in enumerate(placed):
            loc = unreal.Vector(x=x, y=y, z=center.z)
            yaw = rng.uniform(0, 360)
            rot = unreal.Rotator(pitch=0, yaw=yaw, roll=0)

            actor = unreal.EditorLevelLibrary.spawn_actor_from_object(
                asset, loc, rot
            )
            if actor is None:
                continue

            s_min = float(scale_range.get("min", 0.8))
            s_max = float(scale_range.get("max", 1.2))
            s = rng.uniform(s_min, s_max)
            actor.set_actor_scale3d(unreal.Vector(x=s, y=s, z=s))
            actor.set_actor_label(f"WV_DensityFoliage_{i}")

            spawned.append(serialize_actor(actor))

    return {
        "status": "ok",
        "data": {
            "placed": len(spawned),
            "pattern": pattern,
            "actors": spawned,
        },
    }


@register_command("clear_foliage_in_area")
def clear_foliage_in_area(params: dict) -> dict:
    """Remove foliage actors within a rectangular area.

    Parameters
    ----------
    params.area_center : dict
        ``{"x", "y", "z"}`` center of the clear area.
    params.area_size : dict
        ``{"x", "y"}`` width and depth of the area to clear.
    params.class_filter : str, optional
        Only remove actors whose class/label contains this substring.
        Default "foliage" (matches WV_Foliage labels).
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    center = deserialize_vector(params["area_center"])
    area_size = params.get("area_size", {"x": 2000, "y": 2000})
    class_filter = params.get("class_filter", "foliage").lower()

    half_w = float(area_size.get("x", 2000)) / 2
    half_d = float(area_size.get("y", 2000)) / 2

    all_actors = unreal.EditorLevelLibrary.get_all_level_actors()
    to_delete = []

    for actor in all_actors:
        # Check filter
        name = actor.get_actor_label().lower()
        class_name = actor.get_class().get_name().lower()
        if class_filter and class_filter not in name and class_filter not in class_name:
            continue

        loc = actor.get_actor_location()
        if _in_area(loc, center, half_w, half_d):
            to_delete.append(actor)

    deleted_names = []
    subsystem = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)

    with safe_transaction(f"WellVersed: Clear Foliage ({len(to_delete)} actors)"):
        for actor in to_delete:
            try:
                deleted_names.append(actor.get_name())
                subsystem.destroy_actor(actor)
            except Exception:
                pass

    return {
        "status": "ok",
        "data": {
            "deleted": len(deleted_names),
            "deleted_names": deleted_names[:50],
        },
    }


@register_command("randomize_foliage_transforms")
def randomize_foliage_transforms(params: dict) -> dict:
    """Add random variation to existing foliage actor transforms.

    Parameters
    ----------
    params.filter : str, optional
        Only modify actors whose label contains this substring.
        Default "foliage".
    params.scale_min : float, optional
        Minimum uniform scale. Default 0.7.
    params.scale_max : float, optional
        Maximum uniform scale. Default 1.3.
    params.random_rotation : bool, optional
        Randomize yaw rotation. Default True.
    params.position_jitter : float, optional
        Max random offset in X/Y. Default 0 (no jitter).
    params.seed : int, optional
        Random seed.
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    name_filter = params.get("filter", "foliage").lower()
    scale_min = float(params.get("scale_min", 0.7))
    scale_max = float(params.get("scale_max", 1.3))
    random_rotation = params.get("random_rotation", True)
    position_jitter = float(params.get("position_jitter", 0))
    seed = params.get("seed")

    rng = random.Random(seed)
    all_actors = unreal.EditorLevelLibrary.get_all_level_actors()
    modified = []

    with safe_transaction("WellVersed: Randomize Foliage Transforms"):
        for actor in all_actors:
            label = actor.get_actor_label().lower()
            if name_filter and name_filter not in label:
                continue

            # Random scale
            s = rng.uniform(scale_min, scale_max)
            actor.set_actor_scale3d(unreal.Vector(x=s, y=s, z=s))

            # Random rotation
            if random_rotation:
                rot = actor.get_actor_rotation()
                actor.set_actor_rotation(
                    unreal.Rotator(
                        pitch=rot.pitch,
                        yaw=rng.uniform(0, 360),
                        roll=rot.roll,
                    ),
                    teleport=True,
                )

            # Position jitter
            if position_jitter > 0:
                loc = actor.get_actor_location()
                jx = rng.uniform(-position_jitter, position_jitter)
                jy = rng.uniform(-position_jitter, position_jitter)
                actor.set_actor_location(
                    unreal.Vector(x=loc.x + jx, y=loc.y + jy, z=loc.z),
                    sweep=False,
                    teleport=True,
                )

            modified.append(actor.get_name())

    return {
        "status": "ok",
        "data": {
            "modified": len(modified),
            "actor_names": modified[:50],
        },
    }
