"""Zone management -- create, resize, fill, and query spatial zones.

Zones are visible bounding volumes that help organize level layout. They
are represented as scaled cube actors. The bridge can scatter-fill a zone
with actors using Poisson-disk sampling for natural distribution.
"""

from __future__ import annotations

import math
import random

import unreal

from ..listener import register_command
from ..safety import safe_transaction
from ..serialization import serialize_actor, deserialize_vector


# ---------------------------------------------------------------------------
# Internal helpers
# ---------------------------------------------------------------------------

def _find_actor_by_name(name: str) -> unreal.Actor | None:
    for actor in unreal.EditorLevelLibrary.get_all_level_actors():
        if actor.get_name() == name or actor.get_actor_label() == name:
            return actor
    return None


def _get_zone_bounds(actor: unreal.Actor) -> tuple[unreal.Vector, unreal.Vector]:
    """Compute min/max bounds of a zone actor from its location and scale.

    Assumes the zone is a 100-unit cube scaled to the desired size, so
    half-extents = scale * 50.
    """
    loc = actor.get_actor_location()
    scale = actor.get_actor_scale3d()
    half = unreal.Vector(x=scale.x * 50, y=scale.y * 50, z=scale.z * 50)
    min_pt = unreal.Vector(x=loc.x - half.x, y=loc.y - half.y, z=loc.z - half.z)
    max_pt = unreal.Vector(x=loc.x + half.x, y=loc.y + half.y, z=loc.z + half.z)
    return min_pt, max_pt


def _point_in_bounds(
    px: float, py: float, pz: float,
    min_pt: unreal.Vector, max_pt: unreal.Vector,
) -> bool:
    return (
        min_pt.x <= px <= max_pt.x
        and min_pt.y <= py <= max_pt.y
        and min_pt.z <= pz <= max_pt.z
    )


def _poisson_disk_2d(
    min_x: float, max_x: float,
    min_y: float, max_y: float,
    spacing: float,
    max_points: int = 500,
    seed: int | None = None,
) -> list[tuple[float, float]]:
    """Generate 2D Poisson-disk distributed points within a rectangle.

    Uses a simple dart-throwing approach with a cell grid for fast
    neighbor checks.
    """
    rng = random.Random(seed)
    cell_size = spacing / math.sqrt(2)
    cols = max(1, int(math.ceil((max_x - min_x) / cell_size)))
    rows = max(1, int(math.ceil((max_y - min_y) / cell_size)))

    grid: list[list[tuple[float, float] | None]] = [
        [None] * cols for _ in range(rows)
    ]
    points: list[tuple[float, float]] = []

    attempts_per_point = 30

    def _grid_coords(x: float, y: float) -> tuple[int, int]:
        col = int((x - min_x) / cell_size)
        row = int((y - min_y) / cell_size)
        return max(0, min(col, cols - 1)), max(0, min(row, rows - 1))

    def _is_valid(x: float, y: float) -> bool:
        gc, gr = _grid_coords(x, y)
        for dr in range(-2, 3):
            for dc in range(-2, 3):
                nr, nc = gr + dr, gc + dc
                if 0 <= nr < rows and 0 <= nc < cols:
                    neighbor = grid[nr][nc]
                    if neighbor is not None:
                        dx = neighbor[0] - x
                        dy = neighbor[1] - y
                        if dx * dx + dy * dy < spacing * spacing:
                            return False
        return True

    # Seed point
    sx = rng.uniform(min_x, max_x)
    sy = rng.uniform(min_y, max_y)
    points.append((sx, sy))
    gc, gr = _grid_coords(sx, sy)
    grid[gr][gc] = (sx, sy)

    active = [0]
    while active and len(points) < max_points:
        idx = rng.randint(0, len(active) - 1)
        px, py = points[active[idx]]

        found = False
        for _ in range(attempts_per_point):
            angle = rng.uniform(0, 2 * math.pi)
            dist = rng.uniform(spacing, 2 * spacing)
            nx = px + dist * math.cos(angle)
            ny = py + dist * math.sin(angle)

            if min_x <= nx <= max_x and min_y <= ny <= max_y and _is_valid(nx, ny):
                gc, gr = _grid_coords(nx, ny)
                grid[gr][gc] = (nx, ny)
                points.append((nx, ny))
                active.append(len(points) - 1)
                found = True
                break

        if not found:
            active.pop(idx)

    return points


# ---------------------------------------------------------------------------
# Commands
# ---------------------------------------------------------------------------

@register_command("create_zone")
def create_zone(params: dict) -> dict:
    """Create a visible zone volume.

    Parameters
    ----------
    params.location : dict
        Center ``{"x", "y", "z"}``.
    params.size : dict
        Dimensions ``{"x", "y", "z"}`` in Unreal units.
    params.label : str, optional
        Actor label. Default "WV_Zone".
    params.color : dict, optional
        ``{"r", "g", "b", "a"}`` for the zone wireframe color (0-1).
    """
    location = deserialize_vector(params.get("location", {}))
    size = params.get("size", {"x": 1000, "y": 1000, "z": 500})
    label = params.get("label", "WV_Zone")

    # Scale: 100-unit cube -> desired size
    scale = unreal.Vector(
        x=float(size["x"]) / 100,
        y=float(size["y"]) / 100,
        z=float(size["z"]) / 100,
    )

    mesh_path = "/Engine/BasicShapes/Cube.Cube"

    with safe_transaction(f"WellVersed: Create Zone '{label}'"):
        actor = unreal.EditorLevelLibrary.spawn_actor_from_class(
            unreal.StaticMeshActor, location, unreal.Rotator()
        )
        if actor is None:
            raise RuntimeError("Failed to spawn zone actor")

        actor.set_actor_scale3d(scale)
        actor.set_actor_label(label)

        # Set mesh
        mesh = unreal.EditorAssetLibrary.load_asset(mesh_path)
        if mesh is not None:
            component = actor.get_component_by_class(
                unreal.StaticMeshComponent
            )
            if component is not None:
                component.set_static_mesh(mesh)
                # Make it translucent/wireframe so it is visible but not solid
                try:
                    component.set_editor_property(
                        "visible_in_game", False
                    )
                except Exception:
                    pass

        # Tag as a WellVersed zone for later queries
        try:
            actor.tags = [unreal.Name("WellVersed_Zone")]
        except Exception:
            pass

    return serialize_actor(actor)


@register_command("resize_zone_to_selection")
def resize_zone_to_selection(params: dict) -> dict:
    """Resize a zone to fit the currently selected actors.

    Parameters
    ----------
    params.zone_name : str
        Name or label of the zone actor to resize.
    params.padding : float, optional
        Extra padding in all directions. Default 100.
    """
    zone = _find_actor_by_name(params["zone_name"])
    if zone is None:
        raise ValueError(f"Zone not found: {params['zone_name']}")

    padding = float(params.get("padding", 100))
    selected = unreal.EditorLevelLibrary.get_selected_level_actors()

    if not selected:
        raise ValueError("No actors selected to fit zone to")

    # Compute bounds of selected actors
    min_x = min_y = min_z = float("inf")
    max_x = max_y = max_z = float("-inf")

    for actor in selected:
        if actor == zone:
            continue
        loc = actor.get_actor_location()
        min_x = min(min_x, loc.x)
        min_y = min(min_y, loc.y)
        min_z = min(min_z, loc.z)
        max_x = max(max_x, loc.x)
        max_y = max(max_y, loc.y)
        max_z = max(max_z, loc.z)

    if min_x == float("inf"):
        raise ValueError("No valid actors in selection (excluding zone)")

    # Add padding
    min_x -= padding
    min_y -= padding
    min_z -= padding
    max_x += padding
    max_y += padding
    max_z += padding

    center = unreal.Vector(
        x=(min_x + max_x) / 2,
        y=(min_y + max_y) / 2,
        z=(min_z + max_z) / 2,
    )
    size = unreal.Vector(
        x=(max_x - min_x) / 100,
        y=(max_y - min_y) / 100,
        z=(max_z - min_z) / 100,
    )

    with safe_transaction("WellVersed: Resize Zone"):
        zone.set_actor_location(center, sweep=False, teleport=True)
        zone.set_actor_scale3d(size)

    return {
        "zone": zone.get_name(),
        "center": {"x": center.x, "y": center.y, "z": center.z},
        "size": {"x": size.x * 100, "y": size.y * 100, "z": size.z * 100},
    }


@register_command("scatter_fill_zone")
def scatter_fill_zone(params: dict) -> dict:
    """Fill a zone with actors using Poisson-disk distribution.

    Parameters
    ----------
    params.zone_name : str
        Zone actor name.
    params.class_path : str
        Class/asset path to spawn.
    params.spacing : float, optional
        Minimum distance between spawned actors. Default 200.
    params.max_count : int, optional
        Maximum actors to spawn. Default 50.
    params.z_mode : str, optional
        ``"ground"`` (place at zone floor) or ``"random"`` (random Z within
        zone). Default ``"ground"``.
    params.random_rotation : bool, optional
        Randomize yaw. Default True.
    params.random_scale : dict, optional
        ``{"min": 0.8, "max": 1.2}`` for random scale variation.
    params.seed : int, optional
        Random seed for reproducible fills.
    """
    zone = _find_actor_by_name(params["zone_name"])
    if zone is None:
        raise ValueError(f"Zone not found: {params['zone_name']}")

    class_path = params["class_path"]
    spacing = float(params.get("spacing", 200))
    max_count = int(params.get("max_count", 50))
    z_mode = params.get("z_mode", "ground")
    random_rotation = params.get("random_rotation", True)
    scale_range = params.get("random_scale", {})
    seed = params.get("seed")

    min_pt, max_pt = _get_zone_bounds(zone)

    # Generate Poisson-disk points
    points = _poisson_disk_2d(
        min_pt.x, max_pt.x,
        min_pt.y, max_pt.y,
        spacing,
        max_points=max_count,
        seed=seed,
    )

    rng = random.Random(seed)
    spawned = []

    # Load the asset/class once
    asset = unreal.EditorAssetLibrary.load_asset(class_path)

    with safe_transaction(f"WellVersed: Scatter Fill ({len(points)} actors)"):
        for px, py in points:
            if z_mode == "random":
                pz = rng.uniform(min_pt.z, max_pt.z)
            else:
                pz = min_pt.z

            loc = unreal.Vector(x=px, y=py, z=pz)
            yaw = rng.uniform(0, 360) if random_rotation else 0
            rot = unreal.Rotator(pitch=0, yaw=yaw, roll=0)

            if asset is not None:
                actor = unreal.EditorLevelLibrary.spawn_actor_from_object(
                    asset, loc, rot
                )
            else:
                raise ValueError(f"Could not load: {class_path}")

            if actor is None:
                continue

            # Random scale
            if scale_range:
                s_min = float(scale_range.get("min", 1.0))
                s_max = float(scale_range.get("max", 1.0))
                s = rng.uniform(s_min, s_max)
                actor.set_actor_scale3d(unreal.Vector(x=s, y=s, z=s))

            spawned.append(serialize_actor(actor))

    return {
        "zone": zone.get_name(),
        "spawned": len(spawned),
        "actors": spawned,
    }


@register_command("get_zone_contents")
def get_zone_contents(params: dict) -> dict:
    """Find all actors within a zone's bounds.

    Parameters
    ----------
    params.zone_name : str
        Zone actor name.
    params.class_filter : str, optional
        Only return actors whose class name contains this substring.
    """
    zone = _find_actor_by_name(params["zone_name"])
    if zone is None:
        raise ValueError(f"Zone not found: {params['zone_name']}")

    class_filter = params.get("class_filter", "").lower()
    min_pt, max_pt = _get_zone_bounds(zone)

    results = []
    for actor in unreal.EditorLevelLibrary.get_all_level_actors():
        if actor == zone:
            continue
        loc = actor.get_actor_location()
        if not _point_in_bounds(loc.x, loc.y, loc.z, min_pt, max_pt):
            continue
        class_name = actor.get_class().get_name()
        if class_filter and class_filter not in class_name.lower():
            continue
        results.append(serialize_actor(actor))

    return {
        "zone": zone.get_name(),
        "count": len(results),
        "actors": results,
    }
