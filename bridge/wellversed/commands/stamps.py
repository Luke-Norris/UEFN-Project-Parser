"""Stamp system -- save and load reusable actor groups.

Stamps capture a group of selected actors with their relative transforms,
class paths, and properties. They are saved as JSON files in the project's
``Saved/WellVersed/stamps/`` directory and can be placed back into any
level at an arbitrary location.
"""

from __future__ import annotations

import json
import os
import time
from pathlib import Path

import unreal

from ..listener import register_command
from ..safety import safe_transaction
from ..serialization import (
    serialize_actor,
    serialize_value,
    deserialize_vector,
    deserialize_rotator,
    coerce_property_value,
)


# ---------------------------------------------------------------------------
# Stamp directory
# ---------------------------------------------------------------------------

def _stamps_dir() -> Path:
    """Return the stamps directory, creating it if necessary."""
    project_dir = unreal.Paths.project_dir()
    stamps_path = Path(project_dir) / "Saved" / "WellVersed" / "stamps"
    stamps_path.mkdir(parents=True, exist_ok=True)
    return stamps_path


def _stamp_file(name: str) -> Path:
    """Return the path for a named stamp file."""
    safe_name = "".join(
        c if c.isalnum() or c in "-_" else "_" for c in name
    )
    return _stamps_dir() / f"{safe_name}.json"


# ---------------------------------------------------------------------------
# Internal helpers
# ---------------------------------------------------------------------------

def _compute_centroid(actors: list[unreal.Actor]) -> unreal.Vector:
    """Compute the centroid of a list of actors."""
    if not actors:
        return unreal.Vector(0, 0, 0)
    total = unreal.Vector(0, 0, 0)
    for actor in actors:
        loc = actor.get_actor_location()
        total.x += loc.x
        total.y += loc.y
        total.z += loc.z
    n = len(actors)
    return unreal.Vector(total.x / n, total.y / n, total.z / n)


def _capture_actor_data(actor: unreal.Actor, centroid: unreal.Vector) -> dict:
    """Capture an actor's data relative to the centroid."""
    loc = actor.get_actor_location()
    rot = actor.get_actor_rotation()
    scale = actor.get_actor_scale3d()

    data = {
        "class_path": actor.get_class().get_path_name(),
        "label": actor.get_actor_label(),
        "relative_location": {
            "x": loc.x - centroid.x,
            "y": loc.y - centroid.y,
            "z": loc.z - centroid.z,
        },
        "rotation": {
            "pitch": rot.pitch,
            "yaw": rot.yaw,
            "roll": rot.roll,
        },
        "scale": {"x": scale.x, "y": scale.y, "z": scale.z},
    }

    # Capture readable properties
    props = {}
    try:
        for prop_name in actor.get_class().get_editor_property_names():
            prop_str = str(prop_name)
            try:
                val = actor.get_editor_property(prop_str)
                serialized = serialize_value(val)
                if serialized is not None:
                    props[prop_str] = serialized
            except Exception:
                pass
    except Exception:
        pass

    if props:
        data["properties"] = props

    return data


# ---------------------------------------------------------------------------
# Commands
# ---------------------------------------------------------------------------

@register_command("stamp_save")
def stamp_save(params: dict) -> dict:
    """Save selected actors as a reusable stamp.

    Parameters
    ----------
    params.name : str
        Stamp name.
    params.description : str, optional
        Human-readable description.
    """
    name = params["name"]
    description = params.get("description", "")

    actors = unreal.EditorLevelLibrary.get_selected_level_actors()
    if not actors:
        raise ValueError("No actors selected")

    centroid = _compute_centroid(actors)

    stamp_data = {
        "name": name,
        "description": description,
        "version": 1,
        "created": time.time(),
        "actor_count": len(actors),
        "actors": [_capture_actor_data(a, centroid) for a in actors],
    }

    file_path = _stamp_file(name)
    with open(file_path, "w", encoding="utf-8") as f:
        json.dump(stamp_data, f, indent=2)

    unreal.log(f"[WellVersed] Stamp saved: {name} ({len(actors)} actors)")
    return {
        "name": name,
        "actor_count": len(actors),
        "file": str(file_path),
    }


@register_command("stamp_place")
def stamp_place(params: dict) -> dict:
    """Place a saved stamp at a location.

    Parameters
    ----------
    params.name : str
        Stamp name to place.
    params.location : dict, optional
        ``{"x": ..., "y": ..., "z": ...}`` center location. Default origin.
    params.rotation_offset : dict, optional
        ``{"pitch": ..., "yaw": ..., "roll": ...}`` rotation applied to all.
    params.scale_multiplier : float, optional
        Uniform scale multiplier. Default 1.0.
    """
    name = params["name"]
    file_path = _stamp_file(name)

    if not file_path.exists():
        raise ValueError(f"Stamp not found: {name}")

    with open(file_path, "r", encoding="utf-8") as f:
        stamp_data = json.load(f)

    center = deserialize_vector(params.get("location", {}))
    scale_mult = float(params.get("scale_multiplier", 1.0))
    spawned = []

    with safe_transaction(f"WellVersed: Place Stamp '{name}'"):
        for actor_data in stamp_data["actors"]:
            class_path = actor_data["class_path"]
            rel_loc = actor_data["relative_location"]
            loc = unreal.Vector(
                x=center.x + rel_loc["x"] * scale_mult,
                y=center.y + rel_loc["y"] * scale_mult,
                z=center.z + rel_loc["z"] * scale_mult,
            )
            rot = deserialize_rotator(actor_data.get("rotation", {}))

            # Try loading as Blueprint, fall back to native class
            asset = unreal.EditorAssetLibrary.load_asset(class_path)
            if asset is not None:
                actor = unreal.EditorLevelLibrary.spawn_actor_from_object(
                    asset, loc, rot
                )
            else:
                try:
                    actor_class = unreal.EditorAssetLibrary.load_blueprint_class(
                        class_path
                    )
                    actor = unreal.EditorLevelLibrary.spawn_actor_from_class(
                        actor_class, loc, rot
                    )
                except Exception:
                    unreal.log_warning(
                        f"[WellVersed] Could not spawn: {class_path}"
                    )
                    continue

            if actor is None:
                continue

            # Apply scale
            orig_scale = actor_data.get("scale", {"x": 1, "y": 1, "z": 1})
            actor.set_actor_scale3d(unreal.Vector(
                x=orig_scale["x"] * scale_mult,
                y=orig_scale["y"] * scale_mult,
                z=orig_scale["z"] * scale_mult,
            ))

            # Set label
            label = actor_data.get("label")
            if label:
                actor.set_actor_label(label)

            # Restore properties
            for prop_name, prop_value in actor_data.get("properties", {}).items():
                try:
                    coerced = coerce_property_value(prop_value)
                    actor.set_editor_property(prop_name, coerced)
                except Exception:
                    pass

            spawned.append(serialize_actor(actor))

    return {
        "stamp": name,
        "spawned": len(spawned),
        "actors": spawned,
    }


@register_command("stamp_list")
def stamp_list(params: dict) -> dict:
    """List all saved stamps."""
    stamps_dir = _stamps_dir()
    stamps = []

    for file in sorted(stamps_dir.glob("*.json")):
        try:
            with open(file, "r", encoding="utf-8") as f:
                data = json.load(f)
            stamps.append({
                "name": data.get("name", file.stem),
                "description": data.get("description", ""),
                "actor_count": data.get("actor_count", 0),
                "created": data.get("created"),
                "file": str(file),
            })
        except Exception:
            stamps.append({
                "name": file.stem,
                "description": "",
                "actor_count": 0,
                "error": "Could not read stamp file",
            })

    return {"count": len(stamps), "stamps": stamps}


@register_command("stamp_info")
def stamp_info(params: dict) -> dict:
    """Get detailed information about a stamp.

    Parameters
    ----------
    params.name : str
        Stamp name.
    """
    file_path = _stamp_file(params["name"])
    if not file_path.exists():
        raise ValueError(f"Stamp not found: {params['name']}")

    with open(file_path, "r", encoding="utf-8") as f:
        return json.load(f)


@register_command("stamp_delete")
def stamp_delete(params: dict) -> dict:
    """Delete a saved stamp.

    Parameters
    ----------
    params.name : str
        Stamp name to delete.
    """
    file_path = _stamp_file(params["name"])
    if not file_path.exists():
        raise ValueError(f"Stamp not found: {params['name']}")

    os.remove(file_path)
    return {"deleted": params["name"]}


@register_command("stamp_export")
def stamp_export(params: dict) -> dict:
    """Export a stamp as portable JSON (returned directly).

    Parameters
    ----------
    params.name : str
        Stamp name to export.
    """
    file_path = _stamp_file(params["name"])
    if not file_path.exists():
        raise ValueError(f"Stamp not found: {params['name']}")

    with open(file_path, "r", encoding="utf-8") as f:
        data = json.load(f)

    data["_portable"] = True
    return data


_STAMP_CATEGORIES = [
    "gameplay",
    "decoration",
    "spawn_area",
    "shop",
    "obstacle",
    "lighting",
    "audio",
    "structural",
    "utility",
]


@register_command("stamp_set_category")
def stamp_set_category(params: dict) -> dict:
    """Tag a stamp with a category for organization.

    Parameters
    ----------
    params.name : str
        Stamp name.
    params.category : str
        Category: ``"gameplay"``, ``"decoration"``, ``"spawn_area"``,
        ``"shop"``, ``"obstacle"``, ``"lighting"``, ``"audio"``,
        ``"structural"``, ``"utility"``.
    """
    name = params["name"]
    category = params["category"]

    if category not in _STAMP_CATEGORIES:
        available = ", ".join(_STAMP_CATEGORIES)
        raise ValueError(
            f"Unknown category '{category}'. Available: {available}"
        )

    file_path = _stamp_file(name)
    if not file_path.exists():
        raise ValueError(f"Stamp not found: {name}")

    with open(file_path, "r", encoding="utf-8") as f:
        data = json.load(f)

    data["category"] = category

    with open(file_path, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2)

    return {
        "name": name,
        "category": category,
    }


@register_command("stamp_list_by_category")
def stamp_list_by_category(params: dict) -> dict:
    """List stamps filtered by category.

    Parameters
    ----------
    params.category : str
        Category to filter by.
    """
    category = params["category"]
    stamps_dir = _stamps_dir()
    stamps = []

    for file in sorted(stamps_dir.glob("*.json")):
        try:
            with open(file, "r", encoding="utf-8") as f:
                data = json.load(f)
            if data.get("category") == category:
                stamps.append({
                    "name": data.get("name", file.stem),
                    "description": data.get("description", ""),
                    "category": category,
                    "actor_count": data.get("actor_count", 0),
                    "created": data.get("created"),
                })
        except Exception:
            pass

    return {
        "category": category,
        "count": len(stamps),
        "stamps": stamps,
        "available_categories": _STAMP_CATEGORIES,
    }


@register_command("stamp_import")
def stamp_import(params: dict) -> dict:
    """Import a portable stamp JSON.

    Parameters
    ----------
    params.stamp_data : dict
        The full stamp JSON object (as returned by ``stamp_export``).
    params.name : str, optional
        Override the stamp name. Uses the embedded name if omitted.
    """
    stamp_data = params["stamp_data"]
    name = params.get("name") or stamp_data.get("name")
    if not name:
        raise ValueError("Stamp data has no name and none was provided")

    stamp_data["name"] = name
    stamp_data.pop("_portable", None)

    file_path = _stamp_file(name)
    with open(file_path, "w", encoding="utf-8") as f:
        json.dump(stamp_data, f, indent=2)

    return {
        "imported": name,
        "actor_count": stamp_data.get("actor_count", 0),
        "file": str(file_path),
    }
