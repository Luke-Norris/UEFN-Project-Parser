"""Environment commands -- lighting presets, foliage, post-processing.

Provides high-level commands for configuring the level environment:
batch lighting setup, foliage scattering, and post-process configuration.
"""

from __future__ import annotations

import math
import random

import unreal

from ..listener import register_command
from ..safety import safe_transaction
from ..serialization import serialize_actor, deserialize_vector


# ---------------------------------------------------------------------------
# Lighting presets
# ---------------------------------------------------------------------------

_LIGHTING_PRESETS = {
    "daylight": {
        "description": "Bright outdoor daylight",
        "directional": {
            "intensity": 10.0,
            "color": {"r": 1.0, "g": 0.96, "b": 0.88},
            "rotation": {"pitch": -50, "yaw": 160, "roll": 0},
        },
        "skylight_intensity": 2.0,
    },
    "sunset": {
        "description": "Warm sunset lighting",
        "directional": {
            "intensity": 6.0,
            "color": {"r": 1.0, "g": 0.6, "b": 0.3},
            "rotation": {"pitch": -10, "yaw": 200, "roll": 0},
        },
        "skylight_intensity": 1.0,
    },
    "night": {
        "description": "Dark nighttime with moonlight",
        "directional": {
            "intensity": 0.5,
            "color": {"r": 0.6, "g": 0.7, "b": 1.0},
            "rotation": {"pitch": -30, "yaw": 45, "roll": 0},
        },
        "skylight_intensity": 0.2,
    },
    "overcast": {
        "description": "Flat overcast lighting",
        "directional": {
            "intensity": 3.0,
            "color": {"r": 0.85, "g": 0.85, "b": 0.9},
            "rotation": {"pitch": -60, "yaw": 160, "roll": 0},
        },
        "skylight_intensity": 3.0,
    },
    "studio": {
        "description": "Three-point studio lighting",
        "directional": {
            "intensity": 8.0,
            "color": {"r": 1.0, "g": 1.0, "b": 1.0},
            "rotation": {"pitch": -45, "yaw": 135, "roll": 0},
        },
        "skylight_intensity": 1.5,
    },
}


# ---------------------------------------------------------------------------
# Internal helpers
# ---------------------------------------------------------------------------

def _find_actors_of_class(class_name: str) -> list[unreal.Actor]:
    """Find all actors whose class name matches (case-insensitive contains)."""
    results = []
    for actor in unreal.EditorLevelLibrary.get_all_level_actors():
        if class_name.lower() in actor.get_class().get_name().lower():
            results.append(actor)
    return results


# ---------------------------------------------------------------------------
# Commands
# ---------------------------------------------------------------------------

@register_command("set_lighting_preset")
def set_lighting_preset(params: dict) -> dict:
    """Apply a lighting preset to the level.

    Configures directional light(s) and sky light(s) in the scene to match
    the preset. If no lights exist, spawns them.

    Parameters
    ----------
    params.preset : str
        One of: ``"daylight"``, ``"sunset"``, ``"night"``, ``"overcast"``,
        ``"studio"``.
    """
    preset_name = params["preset"].lower()
    preset = _LIGHTING_PRESETS.get(preset_name)
    if preset is None:
        available = ", ".join(sorted(_LIGHTING_PRESETS.keys()))
        raise ValueError(
            f"Unknown preset '{preset_name}'. Available: {available}"
        )

    dir_config = preset["directional"]
    modified = []

    with safe_transaction(f"WellVersed: Lighting Preset '{preset_name}'"):
        # Configure directional lights
        dir_lights = _find_actors_of_class("DirectionalLight")
        if not dir_lights:
            # Spawn one
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
                actor.set_actor_label("WV_DirectionalLight")
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
            except Exception:
                pass

            try:
                comp = light.get_component_by_class(
                    unreal.DirectionalLightComponent
                )
                if comp is not None:
                    comp.set_editor_property("intensity", dir_config["intensity"])
                    comp.set_editor_property(
                        "light_color",
                        unreal.Color(
                            r=int(dir_config["color"]["r"] * 255),
                            g=int(dir_config["color"]["g"] * 255),
                            b=int(dir_config["color"]["b"] * 255),
                            a=255,
                        ),
                    )
            except Exception as exc:
                unreal.log_warning(
                    f"[WellVersed] Could not set light properties: {exc}"
                )

            modified.append(light.get_name())

        # Configure sky lights
        sky_lights = _find_actors_of_class("SkyLight")
        for sky in sky_lights:
            try:
                comp = sky.get_component_by_class(
                    unreal.SkyLightComponent
                )
                if comp is not None:
                    comp.set_editor_property(
                        "intensity", preset["skylight_intensity"]
                    )
            except Exception:
                pass
            modified.append(sky.get_name())

    return {
        "preset": preset_name,
        "description": preset["description"],
        "modified_actors": modified,
    }


@register_command("scatter_foliage")
def scatter_foliage(params: dict) -> dict:
    """Scatter foliage actors across an area.

    Uses random distribution with minimum spacing to place foliage
    instances. For proper instanced foliage, use UEFN's foliage tool;
    this places individual actors for simpler cases.

    Parameters
    ----------
    params.class_path : str
        Asset path to the foliage mesh/blueprint.
    params.center : dict
        ``{"x", "y", "z"}`` center of scatter area.
    params.radius : float
        Scatter radius.
    params.count : int
        Number of instances to place.
    params.min_spacing : float, optional
        Minimum distance between instances. Default 100.
    params.z_offset : float, optional
        Vertical offset from the ground plane. Default 0.
    params.random_scale : dict, optional
        ``{"min": 0.7, "max": 1.3}`` scale range.
    params.random_rotation : bool, optional
        Randomize yaw. Default True.
    params.seed : int, optional
        Random seed.
    """
    class_path = params["class_path"]
    center = deserialize_vector(params["center"])
    radius = float(params["radius"])
    count = int(params["count"])
    min_spacing = float(params.get("min_spacing", 100))
    z_offset = float(params.get("z_offset", 0))
    scale_range = params.get("random_scale", {"min": 0.8, "max": 1.2})
    random_rotation = params.get("random_rotation", True)
    seed = params.get("seed")

    rng = random.Random(seed)
    asset = unreal.EditorAssetLibrary.load_asset(class_path)
    if asset is None:
        raise ValueError(f"Could not load: {class_path}")

    # Generate positions with rejection sampling for spacing
    placed_positions: list[tuple[float, float]] = []
    max_attempts = count * 20

    for _ in range(max_attempts):
        if len(placed_positions) >= count:
            break

        angle = rng.uniform(0, 2 * math.pi)
        dist = rng.uniform(0, radius)
        px = center.x + dist * math.cos(angle)
        py = center.y + dist * math.sin(angle)

        # Check spacing against existing points
        too_close = False
        for ex, ey in placed_positions:
            if (px - ex) ** 2 + (py - ey) ** 2 < min_spacing ** 2:
                too_close = True
                break

        if not too_close:
            placed_positions.append((px, py))

    spawned = []

    with safe_transaction(f"WellVersed: Scatter Foliage ({len(placed_positions)} items)"):
        for i, (px, py) in enumerate(placed_positions):
            loc = unreal.Vector(x=px, y=py, z=center.z + z_offset)
            yaw = rng.uniform(0, 360) if random_rotation else 0
            rot = unreal.Rotator(pitch=0, yaw=yaw, roll=0)

            actor = unreal.EditorLevelLibrary.spawn_actor_from_object(
                asset, loc, rot
            )
            if actor is None:
                continue

            # Random scale
            s_min = float(scale_range.get("min", 0.8))
            s_max = float(scale_range.get("max", 1.2))
            s = rng.uniform(s_min, s_max)
            actor.set_actor_scale3d(unreal.Vector(x=s, y=s, z=s))
            actor.set_actor_label(f"WV_Foliage_{i}")

            spawned.append(serialize_actor(actor))

    return {
        "placed": len(spawned),
        "requested": count,
        "actors": spawned,
    }


@register_command("set_post_process")
def set_post_process(params: dict) -> dict:
    """Configure post-processing settings.

    Finds or spawns a PostProcessVolume and applies settings.

    Parameters
    ----------
    params.bloom_intensity : float, optional
    params.exposure_compensation : float, optional
    params.saturation : float, optional
        Color saturation (1.0 = normal).
    params.contrast : float, optional
        Color contrast (1.0 = normal).
    params.vignette_intensity : float, optional
    params.ambient_occlusion_intensity : float, optional
    """
    modified = {}

    with safe_transaction("WellVersed: Set Post Process"):
        # Find existing PostProcessVolume
        volumes = _find_actors_of_class("PostProcessVolume")
        if volumes:
            volume = volumes[0]
        else:
            # Spawn a global unbound volume
            volume = unreal.EditorLevelLibrary.spawn_actor_from_class(
                unreal.PostProcessVolume,
                unreal.Vector(0, 0, 0),
                unreal.Rotator(),
            )
            if volume is not None:
                volume.set_actor_label("WV_PostProcess")
                try:
                    volume.set_editor_property("unbound", True)
                except Exception:
                    pass

        if volume is None:
            raise RuntimeError("Could not find or create PostProcessVolume")

        # Apply settings via the post process settings struct
        try:
            settings = volume.get_editor_property("settings")
        except Exception:
            settings = None

        setting_map = {
            "bloom_intensity": "bloom_intensity",
            "exposure_compensation": "auto_exposure_bias",
            "vignette_intensity": "vignette_intensity",
            "ambient_occlusion_intensity": "ambient_occlusion_intensity",
        }

        for param_key, prop_name in setting_map.items():
            if param_key in params:
                value = float(params[param_key])
                try:
                    if settings is not None:
                        settings.set_editor_property(prop_name, value)
                    else:
                        volume.set_editor_property(prop_name, value)
                    modified[param_key] = value
                except Exception as exc:
                    modified[param_key] = f"error: {exc}"

        # Saturation and contrast are color grading settings
        for param_key in ("saturation", "contrast"):
            if param_key in params:
                value = float(params[param_key])
                prop_name = f"color_{param_key}"
                try:
                    color_val = unreal.Vector4(
                        x=value, y=value, z=value, w=value
                    )
                    if settings is not None:
                        settings.set_editor_property(prop_name, color_val)
                    else:
                        volume.set_editor_property(prop_name, color_val)
                    modified[param_key] = value
                except Exception as exc:
                    modified[param_key] = f"error: {exc}"

    return {
        "volume": volume.get_name(),
        "modified": modified,
    }
