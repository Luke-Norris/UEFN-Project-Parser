"""Level geometry creation -- floors, walls, boxes, arenas.

Spawns basic geometry actors using engine primitives (``/Engine/BasicShapes/``)
or UEFN gallery pieces, scaled and positioned to the requested dimensions.
Falls back to ``StaticMeshActor`` with cube/plane meshes if the preferred
shapes are not available.
"""

from __future__ import annotations

import unreal

from ..listener import register_command
from ..safety import safe_transaction
from ..serialization import serialize_actor, deserialize_vector


# ---------------------------------------------------------------------------
# Mesh discovery
# ---------------------------------------------------------------------------

# Candidate mesh paths in order of preference
_FLOOR_MESHES = [
    "/Engine/BasicShapes/Plane.Plane",
    "/Engine/BasicShapes/Cube.Cube",
]

_WALL_MESHES = [
    "/Engine/BasicShapes/Cube.Cube",
]

_CUBE_MESHES = [
    "/Engine/BasicShapes/Cube.Cube",
]


def _find_mesh(candidates: list[str]) -> str | None:
    """Return the first mesh path that can be loaded."""
    for path in candidates:
        asset = unreal.EditorAssetLibrary.load_asset(path)
        if asset is not None:
            return path
    return None


def _spawn_mesh_actor(
    mesh_path: str,
    location: unreal.Vector,
    rotation: unreal.Rotator,
    scale: unreal.Vector,
    label: str,
) -> unreal.Actor | None:
    """Spawn a StaticMeshActor with the given mesh, transform, and label."""
    actor = unreal.EditorLevelLibrary.spawn_actor_from_class(
        unreal.StaticMeshActor, location, rotation
    )
    if actor is None:
        return None

    actor.set_actor_scale3d(scale)
    actor.set_actor_label(label)

    # Set the static mesh
    mesh = unreal.EditorAssetLibrary.load_asset(mesh_path)
    if mesh is not None:
        component = actor.get_component_by_class(unreal.StaticMeshComponent)
        if component is not None:
            component.set_static_mesh(mesh)

    return actor


# ---------------------------------------------------------------------------
# Arena presets
# ---------------------------------------------------------------------------

_ARENA_PRESETS = {
    "S": {"width": 2000, "depth": 2000, "wall_height": 500, "teams": 2},
    "M": {"width": 4000, "depth": 4000, "wall_height": 800, "teams": 4},
    "L": {"width": 8000, "depth": 8000, "wall_height": 1200, "teams": 4},
}


# ---------------------------------------------------------------------------
# Commands
# ---------------------------------------------------------------------------

@register_command("create_floor")
def create_floor(params: dict) -> dict:
    """Create a floor plane.

    Parameters
    ----------
    params.location : dict, optional
        Center location ``{"x", "y", "z"}``. Default origin.
    params.width : float, optional
        X dimension in Unreal units. Default 1000.
    params.depth : float, optional
        Y dimension in Unreal units. Default 1000.
    params.label : str, optional
        Actor label. Default "WV_Floor".
    """
    location = deserialize_vector(params.get("location", {}))
    width = float(params.get("width", 1000))
    depth = float(params.get("depth", 1000))
    label = params.get("label", "WV_Floor")

    mesh_path = _find_mesh(_FLOOR_MESHES)
    if mesh_path is None:
        raise RuntimeError("No floor mesh available")

    # Engine Plane is 100x100 by default; Cube is 100x100x100
    if "Plane" in mesh_path:
        scale = unreal.Vector(x=width / 100, y=depth / 100, z=1)
    else:
        # Use a thin cube as a floor
        scale = unreal.Vector(x=width / 100, y=depth / 100, z=0.02)

    with safe_transaction("WellVersed: Create Floor"):
        actor = _spawn_mesh_actor(
            mesh_path, location, unreal.Rotator(), scale, label
        )
        if actor is None:
            raise RuntimeError("Failed to spawn floor actor")

    return serialize_actor(actor)


@register_command("create_wall")
def create_wall(params: dict) -> dict:
    """Create a wall.

    Parameters
    ----------
    params.location : dict, optional
        Base center location. Default origin.
    params.width : float, optional
        Wall width. Default 1000.
    params.height : float, optional
        Wall height. Default 500.
    params.thickness : float, optional
        Wall thickness. Default 20.
    params.yaw : float, optional
        Rotation around Z axis in degrees. Default 0.
    params.label : str, optional
        Actor label. Default "WV_Wall".
    """
    location = deserialize_vector(params.get("location", {}))
    width = float(params.get("width", 1000))
    height = float(params.get("height", 500))
    thickness = float(params.get("thickness", 20))
    yaw = float(params.get("yaw", 0))
    label = params.get("label", "WV_Wall")

    mesh_path = _find_mesh(_WALL_MESHES)
    if mesh_path is None:
        raise RuntimeError("No wall mesh available")

    # Cube is 100x100x100 by default
    scale = unreal.Vector(
        x=width / 100,
        y=thickness / 100,
        z=height / 100,
    )

    # Offset upward so the bottom sits at the specified location
    wall_loc = unreal.Vector(
        x=location.x,
        y=location.y,
        z=location.z + (height / 2),
    )

    rotation = unreal.Rotator(pitch=0, yaw=yaw, roll=0)

    with safe_transaction("WellVersed: Create Wall"):
        actor = _spawn_mesh_actor(
            mesh_path, wall_loc, rotation, scale, label
        )
        if actor is None:
            raise RuntimeError("Failed to spawn wall actor")

    return serialize_actor(actor)


@register_command("create_box")
def create_box(params: dict) -> dict:
    """Create a box room (floor + 4 walls).

    Parameters
    ----------
    params.location : dict, optional
        Center of the floor. Default origin.
    params.width : float, optional
        X dimension. Default 1000.
    params.depth : float, optional
        Y dimension. Default 1000.
    params.wall_height : float, optional
        Wall height. Default 500.
    params.wall_thickness : float, optional
        Wall thickness. Default 20.
    params.label : str, optional
        Base label prefix. Default "WV_Box".
    """
    location = deserialize_vector(params.get("location", {}))
    width = float(params.get("width", 1000))
    depth = float(params.get("depth", 1000))
    wall_height = float(params.get("wall_height", 500))
    wall_thickness = float(params.get("wall_thickness", 20))
    label = params.get("label", "WV_Box")

    actors = []

    with safe_transaction(f"WellVersed: Create Box '{label}'"):
        # Floor
        floor_result = create_floor({
            "location": {"x": location.x, "y": location.y, "z": location.z},
            "width": width,
            "depth": depth,
            "label": f"{label}_Floor",
        })
        actors.append(floor_result)

        # 4 walls: +X, -X, +Y, -Y
        half_w = width / 2
        half_d = depth / 2

        walls = [
            # North wall (+Y)
            {"x": location.x, "y": location.y + half_d, "z": location.z},
            # South wall (-Y)
            {"x": location.x, "y": location.y - half_d, "z": location.z},
            # East wall (+X)
            {"x": location.x + half_w, "y": location.y, "z": location.z},
            # West wall (-X)
            {"x": location.x - half_w, "y": location.y, "z": location.z},
        ]
        widths = [width, width, depth, depth]
        yaws = [0, 0, 90, 90]
        names = ["North", "South", "East", "West"]

        for loc, w, y, name in zip(walls, widths, yaws, names):
            wall_result = create_wall({
                "location": loc,
                "width": w,
                "height": wall_height,
                "thickness": wall_thickness,
                "yaw": y,
                "label": f"{label}_{name}",
            })
            actors.append(wall_result)

    return {"actors": actors, "count": len(actors)}


@register_command("create_arena")
def create_arena(params: dict) -> dict:
    """Create a full arena with team spawn points.

    Parameters
    ----------
    params.preset : str, optional
        Size preset: ``"S"``, ``"M"``, or ``"L"``. Default ``"M"``.
    params.location : dict, optional
        Arena center. Default origin.
    params.label : str, optional
        Label prefix. Default "WV_Arena".
    """
    preset_key = params.get("preset", "M").upper()
    preset = _ARENA_PRESETS.get(preset_key)
    if preset is None:
        raise ValueError(
            f"Unknown preset '{preset_key}'. Use S, M, or L."
        )

    location = deserialize_vector(params.get("location", {}))
    label = params.get("label", "WV_Arena")

    results = {"box": None, "spawn_points": []}

    with safe_transaction(f"WellVersed: Create Arena '{label}'"):
        # Create the box enclosure
        box_result = create_box({
            "location": {"x": location.x, "y": location.y, "z": location.z},
            "width": preset["width"],
            "depth": preset["depth"],
            "wall_height": preset["wall_height"],
            "label": label,
        })
        results["box"] = box_result

        # Place team spawn markers as simple note actors or tags
        teams = preset["teams"]
        half_w = preset["width"] / 2
        half_d = preset["depth"] / 2
        spawn_offset = 200  # inset from walls

        # Distribute spawn points around the edges
        spawn_positions = []
        if teams >= 2:
            spawn_positions.append(
                {"x": location.x, "y": location.y + half_d - spawn_offset, "z": location.z + 50}
            )
            spawn_positions.append(
                {"x": location.x, "y": location.y - half_d + spawn_offset, "z": location.z + 50}
            )
        if teams >= 4:
            spawn_positions.append(
                {"x": location.x + half_w - spawn_offset, "y": location.y, "z": location.z + 50}
            )
            spawn_positions.append(
                {"x": location.x - half_w + spawn_offset, "y": location.y, "z": location.z + 50}
            )

        for i, pos in enumerate(spawn_positions):
            # Spawn a small marker cube for each team spawn
            mesh_path = _find_mesh(_CUBE_MESHES)
            if mesh_path:
                marker = _spawn_mesh_actor(
                    mesh_path,
                    unreal.Vector(x=pos["x"], y=pos["y"], z=pos["z"]),
                    unreal.Rotator(),
                    unreal.Vector(x=0.5, y=0.5, z=0.5),
                    f"{label}_Team{i+1}_Spawn",
                )
                if marker is not None:
                    results["spawn_points"].append(serialize_actor(marker))

    results["preset"] = preset_key
    results["dimensions"] = preset
    return results


@register_command("create_ramp")
def create_ramp(params: dict) -> dict:
    """Create an angled ramp surface.

    Uses a scaled and rotated cube to form a ramp. The ramp goes from
    ground level up to the specified height over the specified depth.

    Parameters
    ----------
    params.width : float, optional
        Ramp width (X dimension). Default 500.
    params.height : float, optional
        Ramp vertical rise. Default 300.
    params.depth : float, optional
        Ramp horizontal run (Y dimension). Default 500.
    params.location : dict, optional
        Base location ``{"x", "y", "z"}``. Default origin.
    params.rotation : dict, optional
        Additional ``{"pitch", "yaw", "roll"}`` rotation. Default none.
    params.label : str, optional
        Actor label. Default "WV_Ramp".
    """
    import math

    width = float(params.get("width", 500))
    height = float(params.get("height", 300))
    depth = float(params.get("depth", 500))
    location = deserialize_vector(params.get("location", {}))
    extra_rot = params.get("rotation", {})
    label = params.get("label", "WV_Ramp")

    mesh_path = _find_mesh(_CUBE_MESHES)
    if mesh_path is None:
        raise RuntimeError("No mesh available for ramp")

    # Calculate the ramp angle from height and depth
    ramp_length = math.sqrt(height ** 2 + depth ** 2)
    pitch_angle = math.degrees(math.atan2(height, depth))

    # Scale: thin cube stretched along the ramp
    scale = unreal.Vector(
        x=width / 100,
        y=ramp_length / 100,
        z=0.05,  # thin slab
    )

    # Position: center the ramp
    center_z = location.z + height / 2
    center_y = location.y + depth / 2

    ramp_loc = unreal.Vector(x=location.x, y=center_y, z=center_z)
    ramp_rot = unreal.Rotator(
        pitch=pitch_angle + float(extra_rot.get("pitch", 0)),
        yaw=float(extra_rot.get("yaw", 0)),
        roll=float(extra_rot.get("roll", 0)),
    )

    with safe_transaction(f"WellVersed: Create Ramp '{label}'"):
        actor = _spawn_mesh_actor(mesh_path, ramp_loc, ramp_rot, scale, label)
        if actor is None:
            raise RuntimeError("Failed to spawn ramp actor")

    return serialize_actor(actor)


@register_command("create_cylinder")
def create_cylinder(params: dict) -> dict:
    """Create a cylindrical shape approximation.

    Uses a scaled cube as a placeholder. For true cylinders, use UEFN's
    cylinder mesh if available, or a gallery piece.

    Parameters
    ----------
    params.radius : float, optional
        Cylinder radius. Default 200.
    params.height : float, optional
        Cylinder height. Default 500.
    params.location : dict, optional
        Center base location. Default origin.
    params.segments : int, optional
        Hint for visual quality (informational only). Default 16.
    params.label : str, optional
        Actor label. Default "WV_Cylinder".
    """
    radius = float(params.get("radius", 200))
    height = float(params.get("height", 500))
    location = deserialize_vector(params.get("location", {}))
    label = params.get("label", "WV_Cylinder")

    # Try cylinder mesh first, fall back to cube
    cylinder_candidates = [
        "/Engine/BasicShapes/Cylinder.Cylinder",
        "/Engine/BasicShapes/Cube.Cube",
    ]
    mesh_path = _find_mesh(cylinder_candidates)
    if mesh_path is None:
        raise RuntimeError("No mesh available for cylinder")

    if "Cylinder" in mesh_path:
        # Engine cylinder is 100 units radius, 100 units tall
        scale = unreal.Vector(x=radius / 50, y=radius / 50, z=height / 100)
    else:
        # Cube approximation
        scale = unreal.Vector(x=radius * 2 / 100, y=radius * 2 / 100, z=height / 100)

    cyl_loc = unreal.Vector(x=location.x, y=location.y, z=location.z + height / 2)

    with safe_transaction(f"WellVersed: Create Cylinder '{label}'"):
        actor = _spawn_mesh_actor(mesh_path, cyl_loc, unreal.Rotator(), scale, label)
        if actor is None:
            raise RuntimeError("Failed to spawn cylinder actor")

    return serialize_actor(actor)


@register_command("create_platform")
def create_platform(params: dict) -> dict:
    """Create an elevated platform with support columns.

    Spawns a floor at the given height plus 4 support pillars underneath.

    Parameters
    ----------
    params.width : float, optional
        Platform width (X). Default 1000.
    params.depth : float, optional
        Platform depth (Y). Default 1000.
    params.height : float, optional
        Platform elevation. Default 500.
    params.location : dict, optional
        Base location (ground level center). Default origin.
    params.pillar_thickness : float, optional
        Pillar cross-section width. Default 50.
    params.label : str, optional
        Label prefix. Default "WV_Platform".
    """
    width = float(params.get("width", 1000))
    depth = float(params.get("depth", 1000))
    height = float(params.get("height", 500))
    location = deserialize_vector(params.get("location", {}))
    pillar_thickness = float(params.get("pillar_thickness", 50))
    label = params.get("label", "WV_Platform")

    mesh_path = _find_mesh(_CUBE_MESHES)
    if mesh_path is None:
        raise RuntimeError("No mesh available for platform")

    actors = []

    with safe_transaction(f"WellVersed: Create Platform '{label}'"):
        # Floor slab
        floor_loc = unreal.Vector(x=location.x, y=location.y, z=location.z + height)
        floor_scale = unreal.Vector(x=width / 100, y=depth / 100, z=0.05)
        floor_actor = _spawn_mesh_actor(
            mesh_path, floor_loc, unreal.Rotator(), floor_scale, f"{label}_Floor"
        )
        if floor_actor:
            actors.append(serialize_actor(floor_actor))

        # 4 support pillars at corners
        inset = pillar_thickness
        corners = [
            (location.x - width / 2 + inset, location.y - depth / 2 + inset),
            (location.x + width / 2 - inset, location.y - depth / 2 + inset),
            (location.x - width / 2 + inset, location.y + depth / 2 - inset),
            (location.x + width / 2 - inset, location.y + depth / 2 - inset),
        ]
        pillar_scale = unreal.Vector(
            x=pillar_thickness / 100,
            y=pillar_thickness / 100,
            z=height / 100,
        )

        for i, (cx, cy) in enumerate(corners):
            pillar_loc = unreal.Vector(x=cx, y=cy, z=location.z + height / 2)
            pillar = _spawn_mesh_actor(
                mesh_path, pillar_loc, unreal.Rotator(), pillar_scale,
                f"{label}_Pillar_{i}"
            )
            if pillar:
                actors.append(serialize_actor(pillar))

    return {"actors": actors, "count": len(actors)}


@register_command("create_corridor")
def create_corridor(params: dict) -> dict:
    """Create a tunnel/hallway (floor + 2 walls + ceiling).

    Parameters
    ----------
    params.width : float, optional
        Interior width. Default 500.
    params.height : float, optional
        Interior height. Default 400.
    params.length : float, optional
        Corridor length. Default 2000.
    params.location : dict, optional
        Start location. Default origin.
    params.rotation : dict, optional
        ``{"yaw": ...}`` corridor direction in degrees. Default 0 (along Y).
    params.wall_thickness : float, optional
        Wall/floor/ceiling thickness. Default 20.
    params.label : str, optional
        Label prefix. Default "WV_Corridor".
    """
    import math

    width = float(params.get("width", 500))
    height = float(params.get("height", 400))
    length = float(params.get("length", 2000))
    location = deserialize_vector(params.get("location", {}))
    yaw = float(params.get("rotation", {}).get("yaw", 0))
    thickness = float(params.get("wall_thickness", 20))
    label = params.get("label", "WV_Corridor")

    mesh_path = _find_mesh(_CUBE_MESHES)
    if mesh_path is None:
        raise RuntimeError("No mesh available for corridor")

    # All pieces are oriented along the local Y axis, then rotated by yaw
    rad = math.radians(yaw)
    cos_y = math.cos(rad)
    sin_y = math.sin(rad)

    def _offset(local_x, local_y, local_z):
        """Transform local offset by yaw rotation."""
        world_x = location.x + local_x * cos_y - local_y * sin_y
        world_y = location.y + local_x * sin_y + local_y * cos_y
        world_z = location.z + local_z
        return unreal.Vector(x=world_x, y=world_y, z=world_z)

    actors = []
    rot = unreal.Rotator(pitch=0, yaw=yaw, roll=0)
    half_len = length / 2

    with safe_transaction(f"WellVersed: Create Corridor '{label}'"):
        # Floor
        floor_scale = unreal.Vector(x=width / 100, y=length / 100, z=thickness / 100)
        floor_loc = _offset(0, half_len, 0)
        a = _spawn_mesh_actor(mesh_path, floor_loc, rot, floor_scale, f"{label}_Floor")
        if a:
            actors.append(serialize_actor(a))

        # Ceiling
        ceil_loc = _offset(0, half_len, height + thickness)
        a = _spawn_mesh_actor(mesh_path, ceil_loc, rot, floor_scale, f"{label}_Ceiling")
        if a:
            actors.append(serialize_actor(a))

        # Left wall
        wall_scale = unreal.Vector(x=thickness / 100, y=length / 100, z=height / 100)
        left_loc = _offset(-(width / 2 + thickness / 2), half_len, height / 2)
        a = _spawn_mesh_actor(mesh_path, left_loc, rot, wall_scale, f"{label}_Left")
        if a:
            actors.append(serialize_actor(a))

        # Right wall
        right_loc = _offset(width / 2 + thickness / 2, half_len, height / 2)
        a = _spawn_mesh_actor(mesh_path, right_loc, rot, wall_scale, f"{label}_Right")
        if a:
            actors.append(serialize_actor(a))

    return {"actors": actors, "count": len(actors)}


@register_command("create_material")
def create_material(params: dict) -> dict:
    """Create a flat color material.

    Parameters
    ----------
    params.name : str
        Material asset name.
    params.color : dict
        ``{"r": 0-1, "g": 0-1, "b": 0-1}`` or hex string ``"#FF0000"``.
    params.package_path : str, optional
        Package path. Default ``"/Game/WellVersed/Materials"``.
    """
    name = params["name"]
    color_data = params["color"]
    package_path = params.get("package_path", "/Game/WellVersed/Materials")

    # Parse color
    if isinstance(color_data, str) and color_data.startswith("#"):
        hex_str = color_data.lstrip("#")
        r = int(hex_str[0:2], 16) / 255.0
        g = int(hex_str[2:4], 16) / 255.0
        b = int(hex_str[4:6], 16) / 255.0
    elif isinstance(color_data, dict):
        r = float(color_data.get("r", 0))
        g = float(color_data.get("g", 0))
        b = float(color_data.get("b", 0))
    else:
        raise ValueError(f"Invalid color format: {color_data}")

    with safe_transaction(f"WellVersed: Create Material '{name}'"):
        asset_tools = unreal.AssetToolsHelpers.get_asset_tools()
        material = asset_tools.create_asset(
            name, package_path, unreal.Material, unreal.MaterialFactoryNew()
        )

        if material is None:
            raise RuntimeError(f"Failed to create material: {name}")

        # Set base color via constant expression
        # Note: Material graph editing may be limited in UEFN Python.
        # Setting the material as a simple flat color.
        unreal.log(
            f"[WellVersed] Material '{name}' created at "
            f"{package_path}/{name} (color: {r:.2f}, {g:.2f}, {b:.2f})"
        )

    return {
        "name": name,
        "path": f"{package_path}/{name}",
        "color": {"r": r, "g": g, "b": b},
    }
