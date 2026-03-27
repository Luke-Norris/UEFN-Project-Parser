"""Actor CRUD operations.

Provides commands for spawning, deleting, duplicating, selecting, and
reading/writing actor properties through ``unreal.EditorLevelLibrary``
and ``unreal.EditorActorSubsystem``.
"""

from __future__ import annotations

from typing import Any

import unreal

from ..listener import register_command
from ..safety import safe_transaction, require_safe_path
from ..serialization import (
    serialize_actor,
    serialize_value,
    deserialize_vector,
    deserialize_rotator,
    coerce_property_value,
)


# ---------------------------------------------------------------------------
# Internal helpers
# ---------------------------------------------------------------------------

def _get_actor_subsystem() -> unreal.EditorActorSubsystem:
    return unreal.get_editor_subsystem(unreal.EditorActorSubsystem)


def _find_actor_by_name(name: str) -> unreal.Actor | None:
    """Find a level actor by its name or label."""
    for actor in unreal.EditorLevelLibrary.get_all_level_actors():
        if actor.get_name() == name or actor.get_actor_label() == name:
            return actor
    return None


def _find_actors_by_names(names: list[str]) -> list[unreal.Actor]:
    """Find multiple actors by name/label."""
    name_set = set(names)
    found = []
    for actor in unreal.EditorLevelLibrary.get_all_level_actors():
        if actor.get_name() in name_set or actor.get_actor_label() in name_set:
            found.append(actor)
    return found


def _get_property_names(actor: unreal.Actor) -> list[str]:
    """Retrieve all editable property names for an actor via its class."""
    names = []
    try:
        for prop in actor.get_class().get_editor_property_names():
            names.append(str(prop))
    except Exception:
        pass
    return names


def _safe_get_properties(actor: unreal.Actor) -> dict:
    """Read all readable properties from an actor.

    Silently skips properties that raise exceptions on read.
    """
    result = {}
    for prop_name in _get_property_names(actor):
        try:
            val = actor.get_editor_property(prop_name)
            result[prop_name] = serialize_value(val)
        except Exception:
            pass
    return result


# ---------------------------------------------------------------------------
# Commands
# ---------------------------------------------------------------------------

@register_command("spawn_actor")
def spawn_actor(params: dict) -> dict:
    """Spawn an actor from a class path or asset path.

    Parameters
    ----------
    params.class_path : str
        Unreal class or Blueprint path, e.g.
        ``"/Script/Engine.PointLight"`` or
        ``"/Game/MyPlugin/BP_MyDevice.BP_MyDevice_C"``.
    params.location : dict, optional
        ``{"x": ..., "y": ..., "z": ...}``. Defaults to origin.
    params.rotation : dict, optional
        ``{"pitch": ..., "yaw": ..., "roll": ...}``. Defaults to zero.
    params.label : str, optional
        Actor label in the outliner.
    params.properties : dict, optional
        Property name/value pairs to set after spawn.
    """
    class_path = params["class_path"]
    require_safe_path(class_path)

    location = deserialize_vector(params.get("location", {}))
    rotation = deserialize_rotator(params.get("rotation", {}))

    with safe_transaction("WellVersed: Spawn Actor"):
        # Try loading as a Blueprint asset first
        asset = unreal.EditorAssetLibrary.load_asset(class_path)
        if asset is not None:
            actor = unreal.EditorLevelLibrary.spawn_actor_from_object(
                asset, location, rotation
            )
        else:
            # Try as a native class
            actor_class = unreal.EditorAssetLibrary.load_blueprint_class(
                class_path
            )
            if actor_class is None:
                raise ValueError(f"Could not load class or asset: {class_path}")
            actor = unreal.EditorLevelLibrary.spawn_actor_from_class(
                actor_class, location, rotation
            )

        if actor is None:
            raise RuntimeError(f"Failed to spawn actor from {class_path}")

        # Set label
        label = params.get("label")
        if label:
            actor.set_actor_label(label)

        # Set additional properties
        properties = params.get("properties", {})
        for prop_name, prop_value in properties.items():
            try:
                coerced = coerce_property_value(prop_value)
                actor.set_editor_property(prop_name, coerced)
            except Exception as exc:
                unreal.log_warning(
                    f"[WellVersed] Could not set {prop_name}: {exc}"
                )

    return serialize_actor(actor)


@register_command("delete_actors")
def delete_actors(params: dict) -> dict:
    """Delete actors by name.

    Parameters
    ----------
    params.names : list[str]
        Actor names or labels to delete.
    """
    names = params["names"]
    actors = _find_actors_by_names(names)

    if not actors:
        return {"deleted": 0, "not_found": names}

    subsystem = _get_actor_subsystem()
    deleted = []
    failed = []

    with safe_transaction("WellVersed: Delete Actors"):
        for actor in actors:
            try:
                subsystem.destroy_actor(actor)
                deleted.append(actor.get_name())
            except Exception as exc:
                failed.append({"name": actor.get_name(), "error": str(exc)})

    found_names = {a.get_name() for a in actors} | {
        a.get_actor_label() for a in actors
    }
    not_found = [n for n in names if n not in found_names]

    return {
        "deleted": len(deleted),
        "deleted_names": deleted,
        "failed": failed,
        "not_found": not_found,
    }


@register_command("set_actor_properties")
def set_actor_properties(params: dict) -> dict:
    """Set properties on an actor.

    Parameters
    ----------
    params.name : str
        Actor name or label.
    params.properties : dict
        Property name/value pairs.
    params.type_hints : dict, optional
        Mapping of property name to type hint for coercion.
    """
    actor = _find_actor_by_name(params["name"])
    if actor is None:
        raise ValueError(f"Actor not found: {params['name']}")

    properties = params["properties"]
    type_hints = params.get("type_hints", {})
    results = {}

    with safe_transaction("WellVersed: Set Properties"):
        for prop_name, prop_value in properties.items():
            try:
                hint = type_hints.get(prop_name)
                coerced = coerce_property_value(prop_value, hint)
                actor.set_editor_property(prop_name, coerced)
                results[prop_name] = "ok"
            except Exception as exc:
                results[prop_name] = f"error: {exc}"

    return {"actor": actor.get_name(), "results": results}


@register_command("get_actor_properties")
def get_actor_properties(params: dict) -> dict:
    """Read properties from an actor.

    Parameters
    ----------
    params.name : str
        Actor name or label.
    params.properties : list[str], optional
        Specific properties to read. If omitted, reads all.
    """
    actor = _find_actor_by_name(params["name"])
    if actor is None:
        raise ValueError(f"Actor not found: {params['name']}")

    requested = params.get("properties")
    if requested:
        result = {}
        for prop_name in requested:
            try:
                val = actor.get_editor_property(prop_name)
                result[prop_name] = serialize_value(val)
            except Exception:
                result[prop_name] = None
    else:
        result = _safe_get_properties(actor)

    return {"actor": actor.get_name(), "properties": result}


@register_command("get_all_actors")
def get_all_actors(params: dict) -> dict:
    """List all actors in the current level.

    Parameters
    ----------
    params.class_filter : str, optional
        Only return actors whose class name contains this substring.
    params.include_properties : bool, optional
        If True, include all readable properties (slower). Default False.
    """
    class_filter = params.get("class_filter", "")
    include_props = params.get("include_properties", False)
    all_actors = unreal.EditorLevelLibrary.get_all_level_actors()

    results = []
    for actor in all_actors:
        class_name = actor.get_class().get_name()
        if class_filter and class_filter.lower() not in class_name.lower():
            continue

        data = serialize_actor(actor)
        if include_props:
            data["properties"] = _safe_get_properties(actor)
        results.append(data)

    return {"count": len(results), "actors": results}


@register_command("duplicate_actors")
def duplicate_actors(params: dict) -> dict:
    """Duplicate actors with an optional offset.

    Parameters
    ----------
    params.names : list[str]
        Actor names or labels to duplicate.
    params.offset : dict, optional
        ``{"x": ..., "y": ..., "z": ...}`` offset from original. Default zero.
    """
    actors = _find_actors_by_names(params["names"])
    if not actors:
        raise ValueError("No matching actors found")

    offset = deserialize_vector(params.get("offset", {}))
    subsystem = _get_actor_subsystem()
    duplicated = []

    with safe_transaction("WellVersed: Duplicate Actors"):
        for actor in actors:
            new_actor = subsystem.duplicate_actor(actor)
            if new_actor is not None:
                loc = new_actor.get_actor_location()
                new_actor.set_actor_location(
                    unreal.Vector(
                        x=loc.x + offset.x,
                        y=loc.y + offset.y,
                        z=loc.z + offset.z,
                    ),
                    sweep=False,
                    teleport=True,
                )
                duplicated.append(serialize_actor(new_actor))

    return {"duplicated": len(duplicated), "actors": duplicated}


@register_command("select_actors")
def select_actors(params: dict) -> dict:
    """Set the editor selection to the named actors.

    Parameters
    ----------
    params.names : list[str]
        Actor names or labels to select.
    """
    actors = _find_actors_by_names(params["names"])
    unreal.EditorLevelLibrary.set_selected_level_actors(actors)
    return {
        "selected": len(actors),
        "names": [a.get_name() for a in actors],
    }


@register_command("get_selected_actors")
def get_selected_actors(params: dict) -> dict:
    """Return the currently selected actors."""
    actors = unreal.EditorLevelLibrary.get_selected_level_actors()
    return {
        "count": len(actors),
        "actors": [serialize_actor(a) for a in actors],
    }
