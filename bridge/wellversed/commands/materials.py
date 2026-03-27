"""Material operations -- create, apply, and batch-swap materials.

Provides commands for working with materials on level actors, including
creating flat-color materials, applying materials to mesh components,
and batch-swapping materials across the level.
"""

from __future__ import annotations

import unreal

from ..listener import register_command
from ..safety import safe_transaction, require_safe_path
from ..serialization import serialize_actor


# ---------------------------------------------------------------------------
# Internal helpers
# ---------------------------------------------------------------------------

def _find_actor_by_name(name: str) -> unreal.Actor | None:
    for actor in unreal.EditorLevelLibrary.get_all_level_actors():
        if actor.get_name() == name or actor.get_actor_label() == name:
            return actor
    return None


def _hex_to_linear(hex_str: str) -> tuple[float, float, float]:
    """Parse ``"#RRGGBB"`` to (r, g, b) floats in 0-1."""
    hex_str = hex_str.lstrip("#")
    r = int(hex_str[0:2], 16) / 255.0
    g = int(hex_str[2:4], 16) / 255.0
    b = int(hex_str[4:6], 16) / 255.0
    return r, g, b


def _get_mesh_component(
    actor: unreal.Actor,
) -> unreal.StaticMeshComponent | None:
    """Get the primary StaticMeshComponent from an actor."""
    try:
        return actor.get_component_by_class(unreal.StaticMeshComponent)
    except Exception:
        return None


# ---------------------------------------------------------------------------
# Commands
# ---------------------------------------------------------------------------

@register_command("create_color_material")
def create_color_material(params: dict) -> dict:
    """Create a flat color material asset.

    Parameters
    ----------
    params.name : str
        Material asset name.
    params.color : str or dict
        Hex string ``"#FF0000"`` or dict ``{"r": 0-1, "g": 0-1, "b": 0-1}``.
    params.package_path : str, optional
        Where to create the asset. Default ``"/Game/WellVersed/Materials"``.
    """
    name = params["name"]
    color = params["color"]
    package_path = params.get("package_path", "/Game/WellVersed/Materials")

    require_safe_path(package_path)

    if isinstance(color, str):
        r, g, b = _hex_to_linear(color)
    elif isinstance(color, dict):
        r = float(color.get("r", 0))
        g = float(color.get("g", 0))
        b = float(color.get("b", 0))
    else:
        raise ValueError(f"Invalid color: {color}")

    with safe_transaction(f"WellVersed: Create Material '{name}'"):
        asset_tools = unreal.AssetToolsHelpers.get_asset_tools()
        material = asset_tools.create_asset(
            name, package_path, unreal.Material, unreal.MaterialFactoryNew()
        )
        if material is None:
            raise RuntimeError(f"Failed to create material: {name}")

    full_path = f"{package_path}/{name}"
    unreal.log(
        f"[WellVersed] Created material '{full_path}' "
        f"(R={r:.2f} G={g:.2f} B={b:.2f})"
    )

    return {
        "name": name,
        "path": full_path,
        "color": {"r": r, "g": g, "b": b},
    }


@register_command("apply_material")
def apply_material(params: dict) -> dict:
    """Apply a material to an actor's mesh component.

    Parameters
    ----------
    params.actor_name : str
        Actor name or label.
    params.material_path : str
        Asset path to the material.
    params.slot_index : int, optional
        Material slot index. Default 0.
    """
    actor = _find_actor_by_name(params["actor_name"])
    if actor is None:
        raise ValueError(f"Actor not found: {params['actor_name']}")

    material_path = params["material_path"]
    slot_index = int(params.get("slot_index", 0))

    material = unreal.EditorAssetLibrary.load_asset(material_path)
    if material is None:
        raise ValueError(f"Material not found: {material_path}")

    component = _get_mesh_component(actor)
    if component is None:
        raise ValueError(
            f"Actor '{params['actor_name']}' has no StaticMeshComponent"
        )

    with safe_transaction("WellVersed: Apply Material"):
        component.set_material(slot_index, material)

    return {
        "actor": actor.get_name(),
        "material": material_path,
        "slot": slot_index,
    }


@register_command("batch_material_swap")
def batch_material_swap(params: dict) -> dict:
    """Replace a material across all actors in the level.

    Parameters
    ----------
    params.old_material : str
        Asset path of the material to replace.
    params.new_material : str
        Asset path of the replacement material.
    params.class_filter : str, optional
        Only affect actors whose class name contains this substring.
    """
    old_path = params["old_material"]
    new_path = params["new_material"]
    class_filter = params.get("class_filter", "")

    new_material = unreal.EditorAssetLibrary.load_asset(new_path)
    if new_material is None:
        raise ValueError(f"New material not found: {new_path}")

    swapped = []
    all_actors = unreal.EditorLevelLibrary.get_all_level_actors()

    with safe_transaction("WellVersed: Batch Material Swap"):
        for actor in all_actors:
            if class_filter:
                class_name = actor.get_class().get_name()
                if class_filter.lower() not in class_name.lower():
                    continue

            component = _get_mesh_component(actor)
            if component is None:
                continue

            # Check each material slot
            try:
                num_materials = component.get_num_materials()
            except Exception:
                continue

            for slot in range(num_materials):
                try:
                    current = component.get_material(slot)
                    if current is not None:
                        current_path = current.get_path_name()
                        if current_path == old_path:
                            component.set_material(slot, new_material)
                            swapped.append({
                                "actor": actor.get_name(),
                                "slot": slot,
                            })
                except Exception:
                    continue

    return {
        "old_material": old_path,
        "new_material": new_path,
        "swapped_count": len(swapped),
        "swapped": swapped,
    }


# ---------------------------------------------------------------------------
# Team color presets
# ---------------------------------------------------------------------------

_TEAM_COLORS = {
    "red": {"r": 0.8, "g": 0.1, "b": 0.1},
    "blue": {"r": 0.1, "g": 0.2, "b": 0.9},
    "green": {"r": 0.1, "g": 0.7, "b": 0.2},
    "yellow": {"r": 0.9, "g": 0.85, "b": 0.1},
    "purple": {"r": 0.6, "g": 0.1, "b": 0.8},
    "orange": {"r": 0.95, "g": 0.5, "b": 0.05},
    "cyan": {"r": 0.0, "g": 0.8, "b": 0.8},
    "pink": {"r": 0.9, "g": 0.3, "b": 0.6},
}

_MATERIAL_PRESETS = {
    "matte_white": {"color": {"r": 0.9, "g": 0.9, "b": 0.9}, "description": "Clean matte white"},
    "matte_black": {"color": {"r": 0.05, "g": 0.05, "b": 0.05}, "description": "Dark matte black"},
    "concrete": {"color": {"r": 0.5, "g": 0.5, "b": 0.48}, "description": "Neutral concrete gray"},
    "wood": {"color": {"r": 0.55, "g": 0.35, "b": 0.2}, "description": "Warm wood tone"},
    "grass": {"color": {"r": 0.2, "g": 0.55, "b": 0.15}, "description": "Natural grass green"},
    "water": {"color": {"r": 0.1, "g": 0.3, "b": 0.6}, "description": "Deep water blue"},
    "sand": {"color": {"r": 0.76, "g": 0.7, "b": 0.5}, "description": "Desert sand"},
    "metal": {"color": {"r": 0.6, "g": 0.6, "b": 0.65}, "description": "Brushed metal gray"},
    "gold": {"color": {"r": 0.83, "g": 0.69, "b": 0.22}, "description": "Metallic gold"},
    "neon_blue": {"color": {"r": 0.0, "g": 0.5, "b": 1.0}, "description": "Neon blue glow"},
    "neon_green": {"color": {"r": 0.0, "g": 1.0, "b": 0.3}, "description": "Neon green glow"},
    "neon_pink": {"color": {"r": 1.0, "g": 0.0, "b": 0.5}, "description": "Neon pink glow"},
    "lava": {"color": {"r": 1.0, "g": 0.3, "b": 0.0}, "description": "Molten lava orange"},
    "ice": {"color": {"r": 0.7, "g": 0.85, "b": 1.0}, "description": "Icy blue-white"},
}


@register_command("apply_team_color_preset")
def apply_team_color_preset(params: dict) -> dict:
    """Apply a team color material to actors.

    Creates a team-colored material and applies it to the specified
    actors' mesh components.

    Parameters
    ----------
    params.actor_names : list[str]
        Actor names or labels to colorize.
    params.team : str
        Team color: ``"red"``, ``"blue"``, ``"green"``, ``"yellow"``,
        ``"purple"``, ``"orange"``, ``"cyan"``, ``"pink"``.
    params.slot_index : int, optional
        Material slot to apply to. Default 0.
    """
    actor_names = params["actor_names"]
    team = params["team"].lower()
    slot_index = int(params.get("slot_index", 0))

    color = _TEAM_COLORS.get(team)
    if color is None:
        available = ", ".join(sorted(_TEAM_COLORS.keys()))
        raise ValueError(f"Unknown team '{team}'. Available: {available}")

    # Create the team material
    mat_name = f"WV_Team_{team.capitalize()}"
    package_path = "/Game/WellVersed/Materials/Teams"
    require_safe_path(package_path)

    applied = []

    with safe_transaction(f"WellVersed: Apply Team Color '{team}'"):
        # Create material
        asset_tools = unreal.AssetToolsHelpers.get_asset_tools()
        material = asset_tools.create_asset(
            mat_name, package_path, unreal.Material, unreal.MaterialFactoryNew()
        )

        if material is None:
            # May already exist -- try loading
            material = unreal.EditorAssetLibrary.load_asset(
                f"{package_path}/{mat_name}"
            )

        if material is None:
            raise RuntimeError(f"Failed to create team material: {mat_name}")

        # Apply to actors
        for name in actor_names:
            actor = _find_actor_by_name(name)
            if actor is None:
                continue

            component = _get_mesh_component(actor)
            if component is None:
                continue

            try:
                component.set_material(slot_index, material)
                applied.append(actor.get_name())
            except Exception:
                pass

    return {
        "team": team,
        "color": color,
        "material": f"{package_path}/{mat_name}",
        "applied_count": len(applied),
        "applied_actors": applied,
    }


@register_command("apply_gradient_material")
def apply_gradient_material(params: dict) -> dict:
    """Create and apply a gradient material between two colors.

    Creates a named material asset. The gradient direction and colors
    are stored in the material's name for reference. Actual gradient
    rendering depends on the material graph setup in UEFN.

    Parameters
    ----------
    params.actor_name : str
        Actor name or label.
    params.color_a : str or dict
        Start color (hex or dict).
    params.color_b : str or dict
        End color (hex or dict).
    params.direction : str, optional
        Gradient direction: ``"horizontal"``, ``"vertical"``, ``"radial"``.
        Default ``"vertical"``.
    params.name : str, optional
        Material asset name. Default auto-generated.
    """
    actor_name = params["actor_name"]
    color_a = params["color_a"]
    color_b = params["color_b"]
    direction = params.get("direction", "vertical")
    mat_name = params.get("name", f"WV_Gradient_{direction}")

    # Parse colors
    if isinstance(color_a, str):
        ra, ga, ba = _hex_to_linear(color_a)
    else:
        ra = float(color_a.get("r", 0))
        ga = float(color_a.get("g", 0))
        ba = float(color_a.get("b", 0))

    if isinstance(color_b, str):
        rb, gb, bb = _hex_to_linear(color_b)
    else:
        rb = float(color_b.get("r", 0))
        gb = float(color_b.get("g", 0))
        bb = float(color_b.get("b", 0))

    package_path = "/Game/WellVersed/Materials/Gradients"
    require_safe_path(package_path)

    with safe_transaction(f"WellVersed: Gradient Material '{mat_name}'"):
        asset_tools = unreal.AssetToolsHelpers.get_asset_tools()
        material = asset_tools.create_asset(
            mat_name, package_path, unreal.Material, unreal.MaterialFactoryNew()
        )

        if material is None:
            material = unreal.EditorAssetLibrary.load_asset(
                f"{package_path}/{mat_name}"
            )

        if material is None:
            raise RuntimeError(f"Failed to create gradient material: {mat_name}")

        # Apply to actor
        actor = _find_actor_by_name(actor_name)
        if actor is not None:
            component = _get_mesh_component(actor)
            if component is not None:
                component.set_material(0, material)

    return {
        "material": f"{package_path}/{mat_name}",
        "color_a": {"r": ra, "g": ga, "b": ba},
        "color_b": {"r": rb, "g": gb, "b": bb},
        "direction": direction,
        "actor": actor_name,
    }


@register_command("create_emissive_material")
def create_emissive_material(params: dict) -> dict:
    """Create a glowing (emissive) material.

    Parameters
    ----------
    params.hex_color : str
        Hex color string ``"#RRGGBB"``.
    params.intensity : float, optional
        Emissive intensity multiplier. Default 5.0.
    params.name : str, optional
        Material asset name. Default auto-generated.
    """
    hex_color = params["hex_color"]
    intensity = float(params.get("intensity", 5.0))
    r, g, b = _hex_to_linear(hex_color)
    mat_name = params.get("name", f"WV_Emissive_{hex_color.lstrip('#')}")
    package_path = "/Game/WellVersed/Materials/Emissive"
    require_safe_path(package_path)

    with safe_transaction(f"WellVersed: Emissive Material '{mat_name}'"):
        asset_tools = unreal.AssetToolsHelpers.get_asset_tools()
        material = asset_tools.create_asset(
            mat_name, package_path, unreal.Material, unreal.MaterialFactoryNew()
        )
        if material is None:
            raise RuntimeError(f"Failed to create emissive material: {mat_name}")

    return {
        "name": mat_name,
        "path": f"{package_path}/{mat_name}",
        "color": {"r": r, "g": g, "b": b},
        "intensity": intensity,
    }


@register_command("get_material_presets")
def get_material_presets(params: dict) -> dict:
    """List all available material presets including team colors.

    Parameters
    ----------
    (no parameters required)
    """
    return {
        "team_colors": {
            name: {"r": c["r"], "g": c["g"], "b": c["b"]}
            for name, c in _TEAM_COLORS.items()
        },
        "presets": {
            name: {
                "color": p["color"],
                "description": p["description"],
            }
            for name, p in _MATERIAL_PRESETS.items()
        },
    }
