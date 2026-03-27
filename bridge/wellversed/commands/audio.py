"""Audio management -- ambient sounds, audio volumes, batch audio control.

Provides commands for creating and configuring audio actors in the level,
including ambient sound sources, audio volumes (3D zones), and batch
volume adjustment.
"""

from __future__ import annotations

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

def _find_actor_by_name(name: str):
    """Find a level actor by its name or label."""
    for actor in unreal.EditorLevelLibrary.get_all_level_actors():
        if actor.get_name() == name or actor.get_actor_label() == name:
            return actor
    return None


def _find_actors_of_class(class_name: str) -> list:
    """Find all actors whose class name matches (case-insensitive contains)."""
    results = []
    for actor in unreal.EditorLevelLibrary.get_all_level_actors():
        if class_name.lower() in actor.get_class().get_name().lower():
            results.append(actor)
    return results


# ---------------------------------------------------------------------------
# Commands
# ---------------------------------------------------------------------------

@register_command("create_ambient_sound")
def create_ambient_sound(params: dict) -> dict:
    """Place an ambient sound actor at a location.

    Parameters
    ----------
    params.location : dict
        ``{"x", "y", "z"}`` position.
    params.sound_asset : str, optional
        Asset path to the sound cue or wave. If omitted, spawns an empty
        AmbientSound that can be configured in UEFN.
    params.volume : float, optional
        Volume multiplier (0-1). Default 1.0.
    params.radius : float, optional
        Attenuation radius (how far the sound reaches). Default 2000.
    params.label : str, optional
        Actor label. Default "WV_AmbientSound".
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    location = deserialize_vector(params["location"])
    sound_asset_path = params.get("sound_asset")
    volume = float(params.get("volume", 1.0))
    radius = float(params.get("radius", 2000))
    label = params.get("label", "WV_AmbientSound")

    with safe_transaction(f"WellVersed: Create Ambient Sound '{label}'"):
        actor = unreal.EditorLevelLibrary.spawn_actor_from_class(
            unreal.AmbientSound, location, unreal.Rotator()
        )
        if actor is None:
            return {"status": "error", "error": "Failed to spawn AmbientSound"}

        actor.set_actor_label(label)

        # Try to set sound asset if provided
        if sound_asset_path:
            try:
                sound = unreal.EditorAssetLibrary.load_asset(sound_asset_path)
                if sound is not None:
                    comp = actor.get_component_by_class(unreal.AudioComponent)
                    if comp is not None:
                        comp.set_editor_property("sound", sound)
            except Exception as exc:
                unreal.log_warning(
                    f"[WellVersed] Could not set sound asset: {exc}"
                )

        # Set volume and attenuation
        try:
            comp = actor.get_component_by_class(unreal.AudioComponent)
            if comp is not None:
                comp.set_editor_property("volume_multiplier", volume)
        except Exception:
            pass

    return {
        "status": "ok",
        "data": {
            **serialize_actor(actor),
            "volume": volume,
            "radius": radius,
        },
    }


@register_command("create_audio_volume")
def create_audio_volume(params: dict) -> dict:
    """Create an audio volume zone (3D area with sound properties).

    Spawns an AudioVolume actor that defines a region where audio
    properties (reverb, volume, etc.) are applied.

    Parameters
    ----------
    params.location : dict
        ``{"x", "y", "z"}`` center of the volume.
    params.size : dict
        ``{"x", "y", "z"}`` dimensions of the volume.
    params.sound_asset : str, optional
        Ambient sound asset to play within the volume.
    params.volume : float, optional
        Volume multiplier (0-1). Default 1.0.
    params.label : str, optional
        Actor label.
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    location = deserialize_vector(params["location"])
    size = params.get("size", {"x": 1000, "y": 1000, "z": 500})
    volume = float(params.get("volume", 1.0))
    label = params.get("label", "WV_AudioVolume")

    with safe_transaction(f"WellVersed: Create Audio Volume '{label}'"):
        # Try AudioVolume class
        actor = None
        try:
            actor = unreal.EditorLevelLibrary.spawn_actor_from_class(
                unreal.AudioVolume, location, unreal.Rotator()
            )
        except Exception:
            pass

        if actor is None:
            # Fall back to a regular TriggerVolume with audio tag
            try:
                actor = unreal.EditorLevelLibrary.spawn_actor_from_class(
                    unreal.TriggerVolume, location, unreal.Rotator()
                )
            except Exception:
                return {"status": "error", "error": "Failed to spawn audio volume"}

        if actor is None:
            return {"status": "error", "error": "Failed to spawn audio volume"}

        actor.set_actor_label(label)

        # Scale to desired size
        scale = unreal.Vector(
            x=float(size.get("x", 1000)) / 100,
            y=float(size.get("y", 1000)) / 100,
            z=float(size.get("z", 500)) / 100,
        )
        actor.set_actor_scale3d(scale)

        # Tag as audio zone
        try:
            actor.tags = [unreal.Name("WellVersed_AudioZone")]
        except Exception:
            pass

    return {
        "status": "ok",
        "data": {
            **serialize_actor(actor),
            "volume": volume,
            "size": size,
        },
    }


@register_command("batch_set_audio_volume")
def batch_set_audio_volume(params: dict) -> dict:
    """Adjust volume on all audio actors matching a filter.

    Parameters
    ----------
    params.filter : str, optional
        Only affect actors whose label contains this substring.
    params.volume : float
        New volume multiplier (0-1).
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    name_filter = params.get("filter", "").lower()
    new_volume = float(params["volume"])

    all_actors = unreal.EditorLevelLibrary.get_all_level_actors()
    modified = []

    with safe_transaction("WellVersed: Batch Set Audio Volume"):
        for actor in all_actors:
            class_name = actor.get_class().get_name().lower()
            label = actor.get_actor_label().lower()

            # Check if this is an audio-related actor
            is_audio = any(kw in class_name for kw in [
                "ambient", "audio", "sound",
            ])
            if not is_audio:
                continue

            if name_filter and name_filter not in label:
                continue

            try:
                comp = actor.get_component_by_class(unreal.AudioComponent)
                if comp is not None:
                    comp.set_editor_property("volume_multiplier", new_volume)
                    modified.append(actor.get_name())
            except Exception:
                pass

    return {
        "status": "ok",
        "data": {
            "modified": len(modified),
            "volume": new_volume,
            "actor_names": modified,
        },
    }


@register_command("get_all_audio_actors")
def get_all_audio_actors(params: dict) -> dict:
    """List all audio source actors in the level.

    Returns all AmbientSound, AudioVolume, and other audio-related
    actors with their properties.

    Parameters
    ----------
    params.filter : str, optional
        Only include actors whose label contains this substring.
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    name_filter = params.get("filter", "").lower()
    all_actors = unreal.EditorLevelLibrary.get_all_level_actors()
    audio_actors = []

    for actor in all_actors:
        class_name = actor.get_class().get_name().lower()
        label = actor.get_actor_label().lower()

        is_audio = any(kw in class_name for kw in [
            "ambient", "audio", "sound",
        ])
        if not is_audio:
            continue

        if name_filter and name_filter not in label:
            continue

        data = serialize_actor(actor)

        # Read audio-specific properties
        try:
            comp = actor.get_component_by_class(unreal.AudioComponent)
            if comp is not None:
                try:
                    data["volume_multiplier"] = comp.get_editor_property(
                        "volume_multiplier"
                    )
                except Exception:
                    pass
                try:
                    sound = comp.get_editor_property("sound")
                    if sound is not None:
                        data["sound_asset"] = sound.get_path_name()
                except Exception:
                    pass
                try:
                    data["is_playing"] = comp.get_editor_property("auto_activate")
                except Exception:
                    pass
        except Exception:
            pass

        audio_actors.append(data)

    return {
        "status": "ok",
        "data": {
            "count": len(audio_actors),
            "actors": audio_actors,
        },
    }
