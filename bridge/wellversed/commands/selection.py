"""Advanced selection tools -- class, radius, property, tag, and set-based selection.

Extends the basic ``select_actors`` command in actors.py with richer
selection mechanisms: spatial queries, property matching, tag filtering,
selection inversion, growth, and named selection sets for quick recall.
"""

from __future__ import annotations

import json
import math
from pathlib import Path

from ..listener import register_command

try:
    import unreal
    from ..serialization import serialize_actor, deserialize_vector
except ImportError:
    unreal = None


# ---------------------------------------------------------------------------
# Internal helpers
# ---------------------------------------------------------------------------

def _selection_sets_dir() -> Path:
    """Return the selection sets directory, creating if needed."""
    project_dir = Path(unreal.Paths.project_dir())
    sets_path = project_dir / "Saved" / "WellVersed" / "selection_sets"
    sets_path.mkdir(parents=True, exist_ok=True)
    return sets_path


def _distance_3d(a, b) -> float:
    dx = a.x - b.x
    dy = a.y - b.y
    dz = a.z - b.z
    return math.sqrt(dx * dx + dy * dy + dz * dz)


# ---------------------------------------------------------------------------
# Commands
# ---------------------------------------------------------------------------

@register_command("select_by_class")
def select_by_class(params: dict) -> dict:
    """Select all actors of a given class.

    Parameters
    ----------
    params.class_name : str
        Class name or substring to match (case-insensitive).
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    class_name = params["class_name"].lower()
    all_actors = unreal.EditorLevelLibrary.get_all_level_actors()

    matching = [
        a for a in all_actors
        if class_name in a.get_class().get_name().lower()
    ]

    unreal.EditorLevelLibrary.set_selected_level_actors(matching)

    return {
        "status": "ok",
        "data": {
            "selected": len(matching),
            "class_filter": params["class_name"],
            "actors": [serialize_actor(a) for a in matching[:50]],
        },
    }


@register_command("select_by_radius")
def select_by_radius(params: dict) -> dict:
    """Select all actors within a radius of a center point.

    Parameters
    ----------
    params.center : dict
        ``{"x", "y", "z"}`` center point.
    params.radius : float
        Selection radius in Unreal units.
    params.class_filter : str, optional
        Only select actors of this class (substring match).
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    center = deserialize_vector(params["center"])
    radius = float(params["radius"])
    class_filter = params.get("class_filter", "").lower()

    all_actors = unreal.EditorLevelLibrary.get_all_level_actors()
    matching = []

    for actor in all_actors:
        loc = actor.get_actor_location()
        if _distance_3d(loc, center) <= radius:
            if class_filter:
                if class_filter not in actor.get_class().get_name().lower():
                    continue
            matching.append(actor)

    unreal.EditorLevelLibrary.set_selected_level_actors(matching)

    return {
        "status": "ok",
        "data": {
            "selected": len(matching),
            "center": {"x": center.x, "y": center.y, "z": center.z},
            "radius": radius,
            "actors": [serialize_actor(a) for a in matching[:50]],
        },
    }


@register_command("select_by_property")
def select_by_property(params: dict) -> dict:
    """Select all actors where a specific property matches a value.

    Parameters
    ----------
    params.property_name : str
        Property name to check.
    params.value : any
        Value to match. Comparison is done via string equality after
        converting both sides.
    params.class_filter : str, optional
        Only check actors of this class (substring).
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    property_name = params["property_name"]
    target_value = str(params["value"])
    class_filter = params.get("class_filter", "").lower()

    all_actors = unreal.EditorLevelLibrary.get_all_level_actors()
    matching = []

    for actor in all_actors:
        if class_filter:
            if class_filter not in actor.get_class().get_name().lower():
                continue

        try:
            val = actor.get_editor_property(property_name)
            if str(val) == target_value:
                matching.append(actor)
        except Exception:
            pass

    unreal.EditorLevelLibrary.set_selected_level_actors(matching)

    return {
        "status": "ok",
        "data": {
            "selected": len(matching),
            "property": property_name,
            "value": target_value,
            "actors": [serialize_actor(a) for a in matching[:50]],
        },
    }


@register_command("select_by_tag")
def select_by_tag(params: dict) -> dict:
    """Select all actors with a specific gameplay tag.

    Parameters
    ----------
    params.tag : str
        Tag string to search for.
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    tag = params["tag"]
    tag_name = unreal.Name(tag)

    all_actors = unreal.EditorLevelLibrary.get_all_level_actors()
    matching = []

    for actor in all_actors:
        try:
            if hasattr(actor, "tags") and tag_name in actor.tags:
                matching.append(actor)
        except Exception:
            pass

    unreal.EditorLevelLibrary.set_selected_level_actors(matching)

    return {
        "status": "ok",
        "data": {
            "selected": len(matching),
            "tag": tag,
            "actors": [serialize_actor(a) for a in matching[:50]],
        },
    }


@register_command("invert_selection")
def invert_selection(params: dict) -> dict:
    """Invert the current editor selection.

    Selects everything that is NOT currently selected, and deselects
    everything that is.

    Parameters
    ----------
    params.class_filter : str, optional
        Only consider actors of this class for the inversion.
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    class_filter = params.get("class_filter", "").lower()

    currently_selected = set(
        a.get_name()
        for a in unreal.EditorLevelLibrary.get_selected_level_actors()
    )

    all_actors = unreal.EditorLevelLibrary.get_all_level_actors()
    new_selection = []

    for actor in all_actors:
        if class_filter:
            if class_filter not in actor.get_class().get_name().lower():
                continue

        if actor.get_name() not in currently_selected:
            new_selection.append(actor)

    unreal.EditorLevelLibrary.set_selected_level_actors(new_selection)

    return {
        "status": "ok",
        "data": {
            "previously_selected": len(currently_selected),
            "now_selected": len(new_selection),
        },
    }


@register_command("grow_selection")
def grow_selection(params: dict) -> dict:
    """Expand the current selection to include nearby actors.

    Parameters
    ----------
    params.distance : float
        Maximum distance from any currently selected actor to include
        new actors. Default 500.
    params.class_filter : str, optional
        Only grow to include actors of this class (substring).
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    distance = float(params.get("distance", 500))
    class_filter = params.get("class_filter", "").lower()

    currently_selected = unreal.EditorLevelLibrary.get_selected_level_actors()
    if not currently_selected:
        return {"status": "ok", "data": {"selected": 0, "message": "No actors currently selected"}}

    selected_set = set(a.get_name() for a in currently_selected)
    all_actors = unreal.EditorLevelLibrary.get_all_level_actors()

    # Get positions of selected actors
    selected_positions = [a.get_actor_location() for a in currently_selected]

    new_selection = list(currently_selected)

    for actor in all_actors:
        if actor.get_name() in selected_set:
            continue

        if class_filter:
            if class_filter not in actor.get_class().get_name().lower():
                continue

        loc = actor.get_actor_location()
        for sel_pos in selected_positions:
            if _distance_3d(loc, sel_pos) <= distance:
                new_selection.append(actor)
                break

    unreal.EditorLevelLibrary.set_selected_level_actors(new_selection)

    return {
        "status": "ok",
        "data": {
            "previously_selected": len(currently_selected),
            "now_selected": len(new_selection),
            "added": len(new_selection) - len(currently_selected),
            "distance": distance,
        },
    }


@register_command("save_selection_set")
def save_selection_set(params: dict) -> dict:
    """Save the current selection as a named set for later recall.

    Parameters
    ----------
    params.name : str
        Name for the selection set.
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    name = params["name"]
    selected = unreal.EditorLevelLibrary.get_selected_level_actors()

    if not selected:
        return {"status": "error", "error": "No actors selected"}

    actor_names = [a.get_name() for a in selected]
    actor_labels = [a.get_actor_label() for a in selected]

    set_data = {
        "name": name,
        "actor_count": len(actor_names),
        "actors": [
            {"name": n, "label": l}
            for n, l in zip(actor_names, actor_labels)
        ],
    }

    safe_name = "".join(c if c.isalnum() or c in "-_" else "_" for c in name)
    file_path = _selection_sets_dir() / f"{safe_name}.json"

    with open(file_path, "w", encoding="utf-8") as f:
        json.dump(set_data, f, indent=2)

    return {
        "status": "ok",
        "data": {
            "name": name,
            "actor_count": len(actor_names),
            "file": str(file_path),
        },
    }


@register_command("load_selection_set")
def load_selection_set(params: dict) -> dict:
    """Restore a previously saved selection set.

    Parameters
    ----------
    params.name : str
        Name of the selection set to load.
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    name = params["name"]
    safe_name = "".join(c if c.isalnum() or c in "-_" else "_" for c in name)
    file_path = _selection_sets_dir() / f"{safe_name}.json"

    if not file_path.exists():
        return {"status": "error", "error": f"Selection set not found: {name}"}

    with open(file_path, "r", encoding="utf-8") as f:
        set_data = json.load(f)

    actor_names = {a["name"] for a in set_data.get("actors", [])}
    actor_labels = {a["label"] for a in set_data.get("actors", [])}

    all_actors = unreal.EditorLevelLibrary.get_all_level_actors()
    matching = []

    for actor in all_actors:
        if actor.get_name() in actor_names or actor.get_actor_label() in actor_labels:
            matching.append(actor)

    unreal.EditorLevelLibrary.set_selected_level_actors(matching)

    return {
        "status": "ok",
        "data": {
            "name": name,
            "requested": len(actor_names),
            "found": len(matching),
            "missing": len(actor_names) - len(matching),
        },
    }
