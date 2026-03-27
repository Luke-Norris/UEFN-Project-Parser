"""Advanced lighting -- point lights, spotlights, directional lights, presets.

Dedicated lighting module with commands to create and configure individual
light actors, batch-modify light properties, and apply world lighting
presets. Goes beyond the basic presets in environment.py.
"""

from __future__ import annotations

from ..listener import register_command
from ..safety import safe_transaction

try:
    import unreal
    from ..serialization import serialize_actor, deserialize_vector, deserialize_rotator
except ImportError:
    unreal = None


# ---------------------------------------------------------------------------
# World lighting presets (expanded set)
# ---------------------------------------------------------------------------

_WORLD_PRESETS = {
    "sunny": {
        "description": "Bright clear day with warm sunlight",
        "directional": {"intensity": 10.0, "color": {"r": 255, "g": 245, "b": 225}, "rotation": {"pitch": -50, "yaw": 160, "roll": 0}},
        "skylight_intensity": 2.0,
    },
    "sunset": {
        "description": "Golden hour with long shadows",
        "directional": {"intensity": 6.0, "color": {"r": 255, "g": 150, "b": 75}, "rotation": {"pitch": -10, "yaw": 200, "roll": 0}},
        "skylight_intensity": 1.0,
    },
    "night": {
        "description": "Dark night with soft moonlight",
        "directional": {"intensity": 0.5, "color": {"r": 150, "g": 180, "b": 255}, "rotation": {"pitch": -30, "yaw": 45, "roll": 0}},
        "skylight_intensity": 0.15,
    },
    "foggy": {
        "description": "Dense fog with diffused lighting",
        "directional": {"intensity": 2.0, "color": {"r": 200, "g": 200, "b": 210}, "rotation": {"pitch": -60, "yaw": 160, "roll": 0}},
        "skylight_intensity": 4.0,
    },
    "arena_bright": {
        "description": "Even bright lighting for competitive play",
        "directional": {"intensity": 8.0, "color": {"r": 255, "g": 255, "b": 255}, "rotation": {"pitch": -70, "yaw": 0, "roll": 0}},
        "skylight_intensity": 3.0,
    },
    "horror": {
        "description": "Dark atmosphere with subtle cold tones",
        "directional": {"intensity": 0.3, "color": {"r": 100, "g": 120, "b": 180}, "rotation": {"pitch": -20, "yaw": 90, "roll": 0}},
        "skylight_intensity": 0.05,
    },
}


# ---------------------------------------------------------------------------
# Internal helpers
# ---------------------------------------------------------------------------

def _find_actors_of_class(class_name: str) -> list:
    """Find all actors whose class name matches (case-insensitive contains)."""
    results = []
    for actor in unreal.EditorLevelLibrary.get_all_level_actors():
        if class_name.lower() in actor.get_class().get_name().lower():
            results.append(actor)
    return results


def _hex_to_color(hex_str: str) -> tuple[int, int, int]:
    """Parse ``"#RRGGBB"`` to (R, G, B) ints 0-255."""
    hex_str = hex_str.lstrip("#")
    return (
        int(hex_str[0:2], 16),
        int(hex_str[2:4], 16),
        int(hex_str[4:6], 16),
    )


def _parse_color(color_data) -> unreal.Color:
    """Parse a color from hex string or dict to unreal.Color."""
    if isinstance(color_data, str) and color_data.startswith("#"):
        r, g, b = _hex_to_color(color_data)
    elif isinstance(color_data, dict):
        r = int(color_data.get("r", 255))
        g = int(color_data.get("g", 255))
        b = int(color_data.get("b", 255))
    else:
        r, g, b = 255, 255, 255
    return unreal.Color(r=r, g=g, b=b, a=255)


# ---------------------------------------------------------------------------
# Commands
# ---------------------------------------------------------------------------

@register_command("create_point_light")
def create_point_light(params: dict) -> dict:
    """Spawn a point light actor.

    Parameters
    ----------
    params.location : dict
        ``{"x", "y", "z"}`` position.
    params.color : str or dict, optional
        Hex ``"#RRGGBB"`` or dict ``{"r", "g", "b"}`` (0-255). Default white.
    params.intensity : float, optional
        Light intensity. Default 5000.
    params.radius : float, optional
        Attenuation radius. Default 1000.
    params.label : str, optional
        Actor label. Default "WV_PointLight".
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    location = deserialize_vector(params["location"])
    color = _parse_color(params.get("color", {"r": 255, "g": 255, "b": 255}))
    intensity = float(params.get("intensity", 5000))
    attn_radius = float(params.get("radius", 1000))
    label = params.get("label", "WV_PointLight")

    with safe_transaction(f"WellVersed: Create Point Light '{label}'"):
        actor = unreal.EditorLevelLibrary.spawn_actor_from_class(
            unreal.PointLight, location, unreal.Rotator()
        )
        if actor is None:
            return {"status": "error", "error": "Failed to spawn PointLight"}

        actor.set_actor_label(label)

        try:
            comp = actor.get_component_by_class(unreal.PointLightComponent)
            if comp is not None:
                comp.set_editor_property("intensity", intensity)
                comp.set_editor_property("light_color", color)
                comp.set_editor_property("attenuation_radius", attn_radius)
        except Exception as exc:
            unreal.log_warning(f"[WellVersed] Light property error: {exc}")

    return {
        "status": "ok",
        "data": serialize_actor(actor),
    }


@register_command("create_spot_light")
def create_spot_light(params: dict) -> dict:
    """Spawn a spotlight actor.

    Parameters
    ----------
    params.location : dict
        ``{"x", "y", "z"}`` position.
    params.rotation : dict, optional
        ``{"pitch", "yaw", "roll"}``. Default points downward.
    params.color : str or dict, optional
        Hex or dict color. Default white.
    params.intensity : float, optional
        Light intensity. Default 5000.
    params.inner_angle : float, optional
        Inner cone angle in degrees. Default 25.
    params.outer_angle : float, optional
        Outer cone angle in degrees. Default 44.
    params.label : str, optional
        Actor label.
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    location = deserialize_vector(params["location"])
    rotation = deserialize_rotator(params.get("rotation", {"pitch": -90, "yaw": 0, "roll": 0}))
    color = _parse_color(params.get("color", {"r": 255, "g": 255, "b": 255}))
    intensity = float(params.get("intensity", 5000))
    inner_angle = float(params.get("inner_angle", 25))
    outer_angle = float(params.get("outer_angle", 44))
    label = params.get("label", "WV_SpotLight")

    with safe_transaction(f"WellVersed: Create Spotlight '{label}'"):
        actor = unreal.EditorLevelLibrary.spawn_actor_from_class(
            unreal.SpotLight, location, rotation
        )
        if actor is None:
            return {"status": "error", "error": "Failed to spawn SpotLight"}

        actor.set_actor_label(label)

        try:
            comp = actor.get_component_by_class(unreal.SpotLightComponent)
            if comp is not None:
                comp.set_editor_property("intensity", intensity)
                comp.set_editor_property("light_color", color)
                comp.set_editor_property("inner_cone_angle", inner_angle)
                comp.set_editor_property("outer_cone_angle", outer_angle)
        except Exception as exc:
            unreal.log_warning(f"[WellVersed] Spotlight property error: {exc}")

    return {
        "status": "ok",
        "data": serialize_actor(actor),
    }


@register_command("create_directional_light")
def create_directional_light(params: dict) -> dict:
    """Spawn a directional (sun/moon) light actor.

    Parameters
    ----------
    params.rotation : dict, optional
        ``{"pitch", "yaw", "roll"}``. Default sun-like angle.
    params.color : str or dict, optional
        Hex or dict color. Default warm white.
    params.intensity : float, optional
        Light intensity. Default 10.
    params.label : str, optional
        Actor label.
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    rotation = deserialize_rotator(params.get("rotation", {"pitch": -50, "yaw": 160, "roll": 0}))
    color = _parse_color(params.get("color", {"r": 255, "g": 245, "b": 225}))
    intensity = float(params.get("intensity", 10.0))
    label = params.get("label", "WV_DirectionalLight")

    with safe_transaction(f"WellVersed: Create Directional Light '{label}'"):
        actor = unreal.EditorLevelLibrary.spawn_actor_from_class(
            unreal.DirectionalLight,
            unreal.Vector(0, 0, 1000),
            rotation,
        )
        if actor is None:
            return {"status": "error", "error": "Failed to spawn DirectionalLight"}

        actor.set_actor_label(label)

        try:
            comp = actor.get_component_by_class(unreal.DirectionalLightComponent)
            if comp is not None:
                comp.set_editor_property("intensity", intensity)
                comp.set_editor_property("light_color", color)
        except Exception as exc:
            unreal.log_warning(f"[WellVersed] Directional light error: {exc}")

    return {
        "status": "ok",
        "data": serialize_actor(actor),
    }


@register_command("batch_set_light_properties")
def batch_set_light_properties(params: dict) -> dict:
    """Modify properties on all lights matching a filter.

    Parameters
    ----------
    params.filter : str, optional
        Only affect lights whose label contains this substring.
    params.class_type : str, optional
        Light class type: "point", "spot", "directional", or "all".
        Default "all".
    params.intensity : float, optional
        Set intensity on matching lights.
    params.color : str or dict, optional
        Set color on matching lights.
    params.radius : float, optional
        Set attenuation radius (point/spot lights only).
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    name_filter = params.get("filter", "").lower()
    class_type = params.get("class_type", "all").lower()
    new_intensity = params.get("intensity")
    new_color = params.get("color")
    new_radius = params.get("radius")

    # Map class type to search terms
    class_map = {
        "point": "PointLight",
        "spot": "SpotLight",
        "directional": "DirectionalLight",
        "all": "Light",
    }
    search_term = class_map.get(class_type, "Light")

    all_actors = unreal.EditorLevelLibrary.get_all_level_actors()
    modified = []

    with safe_transaction("WellVersed: Batch Set Light Properties"):
        for actor in all_actors:
            class_name = actor.get_class().get_name()
            if search_term.lower() not in class_name.lower():
                continue

            if name_filter and name_filter not in actor.get_actor_label().lower():
                continue

            # Try to find any light component
            comp = None
            for comp_class in [
                unreal.PointLightComponent,
                unreal.SpotLightComponent,
                unreal.DirectionalLightComponent,
            ]:
                try:
                    comp = actor.get_component_by_class(comp_class)
                    if comp is not None:
                        break
                except Exception:
                    continue

            if comp is None:
                continue

            try:
                if new_intensity is not None:
                    comp.set_editor_property("intensity", float(new_intensity))
                if new_color is not None:
                    comp.set_editor_property("light_color", _parse_color(new_color))
                if new_radius is not None:
                    try:
                        comp.set_editor_property("attenuation_radius", float(new_radius))
                    except Exception:
                        pass  # Directional lights don't have radius

                modified.append(actor.get_name())
            except Exception as exc:
                unreal.log_warning(
                    f"[WellVersed] Could not set light props on {actor.get_name()}: {exc}"
                )

    return {
        "status": "ok",
        "data": {
            "modified": len(modified),
            "actor_names": modified,
        },
    }


@register_command("set_world_lighting_preset")
def set_world_lighting_preset(params: dict) -> dict:
    """Apply an expanded world lighting preset.

    Parameters
    ----------
    params.preset : str
        One of: "sunny", "sunset", "night", "foggy", "arena_bright", "horror".
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    preset_name = params["preset"].lower()
    preset = _WORLD_PRESETS.get(preset_name)
    if preset is None:
        available = ", ".join(sorted(_WORLD_PRESETS.keys()))
        return {"status": "error", "error": f"Unknown preset '{preset_name}'. Available: {available}"}

    dir_config = preset["directional"]
    modified = []

    with safe_transaction(f"WellVersed: World Lighting '{preset_name}'"):
        # Directional lights
        dir_lights = _find_actors_of_class("DirectionalLight")
        if not dir_lights:
            actor = unreal.EditorLevelLibrary.spawn_actor_from_class(
                unreal.DirectionalLight,
                unreal.Vector(0, 0, 1000),
                unreal.Rotator(
                    pitch=dir_config["rotation"]["pitch"],
                    yaw=dir_config["rotation"]["yaw"],
                    roll=dir_config["rotation"]["roll"],
                ),
            )
            if actor is not None:
                actor.set_actor_label("WV_Sun")
                dir_lights = [actor]

        for light in dir_lights:
            try:
                light.set_actor_rotation(
                    unreal.Rotator(
                        pitch=dir_config["rotation"]["pitch"],
                        yaw=dir_config["rotation"]["yaw"],
                        roll=dir_config["rotation"]["roll"],
                    ),
                    teleport=True,
                )
                comp = light.get_component_by_class(unreal.DirectionalLightComponent)
                if comp is not None:
                    comp.set_editor_property("intensity", dir_config["intensity"])
                    comp.set_editor_property("light_color", unreal.Color(
                        r=dir_config["color"]["r"],
                        g=dir_config["color"]["g"],
                        b=dir_config["color"]["b"],
                        a=255,
                    ))
                modified.append(light.get_name())
            except Exception:
                pass

        # Sky lights
        sky_lights = _find_actors_of_class("SkyLight")
        for sky in sky_lights:
            try:
                comp = sky.get_component_by_class(unreal.SkyLightComponent)
                if comp is not None:
                    comp.set_editor_property("intensity", preset["skylight_intensity"])
                modified.append(sky.get_name())
            except Exception:
                pass

    return {
        "status": "ok",
        "data": {
            "preset": preset_name,
            "description": preset["description"],
            "modified_actors": modified,
        },
    }


@register_command("get_all_lights")
def get_all_lights(params: dict) -> dict:
    """List all light actors in the level with their properties.

    Parameters
    ----------
    params.class_type : str, optional
        Filter by type: "point", "spot", "directional", or "all".
        Default "all".
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    class_type = params.get("class_type", "all").lower()

    all_actors = unreal.EditorLevelLibrary.get_all_level_actors()
    lights = []

    for actor in all_actors:
        class_name = actor.get_class().get_name()
        class_lower = class_name.lower()

        # Identify light types
        if "light" not in class_lower:
            continue

        light_type = "unknown"
        if "pointlight" in class_lower:
            light_type = "point"
        elif "spotlight" in class_lower:
            light_type = "spot"
        elif "directionallight" in class_lower:
            light_type = "directional"
        elif "skylight" in class_lower:
            light_type = "sky"
        elif "rectlight" in class_lower:
            light_type = "rect"

        if class_type != "all" and light_type != class_type:
            continue

        data = serialize_actor(actor)
        data["light_type"] = light_type

        # Read light-specific properties
        for comp_class in [
            unreal.PointLightComponent,
            unreal.SpotLightComponent,
            unreal.DirectionalLightComponent,
            unreal.SkyLightComponent,
        ]:
            try:
                comp = actor.get_component_by_class(comp_class)
                if comp is not None:
                    try:
                        data["intensity"] = comp.get_editor_property("intensity")
                    except Exception:
                        pass
                    try:
                        color = comp.get_editor_property("light_color")
                        data["color"] = {"r": color.r, "g": color.g, "b": color.b}
                    except Exception:
                        pass
                    try:
                        data["attenuation_radius"] = comp.get_editor_property("attenuation_radius")
                    except Exception:
                        pass
                    break
            except Exception:
                continue

        lights.append(data)

    return {
        "status": "ok",
        "data": {
            "count": len(lights),
            "lights": lights,
        },
    }
