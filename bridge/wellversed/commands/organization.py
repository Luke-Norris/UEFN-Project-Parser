"""Asset/actor organization -- renaming, folders, grouping, tagging.

Provides commands for organizing actors in the level: batch renaming
by convention, sorting into editor folders, grouping, and gameplay
tag management.
"""

from __future__ import annotations

import re

from ..listener import register_command
from ..safety import safe_transaction

try:
    import unreal
    from ..serialization import serialize_actor
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


def _find_actors_by_names(names: list[str]) -> list:
    """Find multiple actors by name/label."""
    name_set = set(names)
    found = []
    for actor in unreal.EditorLevelLibrary.get_all_level_actors():
        if actor.get_name() in name_set or actor.get_actor_label() in name_set:
            found.append(actor)
    return found


# ---------------------------------------------------------------------------
# Commands
# ---------------------------------------------------------------------------

@register_command("rename_actors_by_convention")
def rename_actors_by_convention(params: dict) -> dict:
    """Batch rename actors matching a pattern with sequential numbering.

    Parameters
    ----------
    params.pattern : str
        Substring filter -- only actors whose current label contains this
        string (case-insensitive) will be renamed.
    params.prefix : str
        New label prefix, e.g. ``"SpawnPad"``.
    params.start_index : int, optional
        Starting number. Default 1.
    params.zero_pad : int, optional
        Zero-pad width for the index. Default 2 (e.g., ``01``, ``02``).
    params.separator : str, optional
        Character between prefix and index. Default ``"_"``.
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    pattern = params["pattern"].lower()
    prefix = params["prefix"]
    start_index = int(params.get("start_index", 1))
    zero_pad = int(params.get("zero_pad", 2))
    separator = params.get("separator", "_")

    all_actors = unreal.EditorLevelLibrary.get_all_level_actors()
    matching = []

    for actor in all_actors:
        label = actor.get_actor_label().lower()
        class_name = actor.get_class().get_name().lower()
        if pattern in label or pattern in class_name:
            matching.append(actor)

    if not matching:
        return {"status": "ok", "data": {"renamed": 0, "message": "No actors matched the pattern"}}

    renamed = []

    with safe_transaction(f"WellVersed: Rename {len(matching)} actors"):
        for i, actor in enumerate(matching):
            idx = start_index + i
            new_label = f"{prefix}{separator}{str(idx).zfill(zero_pad)}"
            old_label = actor.get_actor_label()
            actor.set_actor_label(new_label)
            renamed.append({
                "old_label": old_label,
                "new_label": new_label,
                "name": actor.get_name(),
            })

    return {
        "status": "ok",
        "data": {
            "renamed": len(renamed),
            "actors": renamed,
        },
    }


@register_command("sort_into_folders")
def sort_into_folders(params: dict) -> dict:
    """Organize actors into editor folders by type or class pattern.

    Parameters
    ----------
    params.rules : list[dict]
        List of rules, each with:
        - ``pattern`` (str): class name substring to match
        - ``folder`` (str): folder path (e.g., ``"Lighting"``, ``"Devices/Spawners"``)
    params.default_folder : str, optional
        Folder for actors that don't match any rule. If omitted,
        unmatched actors are left in place.
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    rules = params.get("rules", [])
    default_folder = params.get("default_folder")

    if not rules:
        return {"status": "error", "error": "No rules provided"}

    all_actors = unreal.EditorLevelLibrary.get_all_level_actors()
    moved = []

    with safe_transaction("WellVersed: Sort Into Folders"):
        for actor in all_actors:
            class_name = actor.get_class().get_name().lower()
            label = actor.get_actor_label().lower()
            target_folder = None

            for rule in rules:
                pattern = rule.get("pattern", "").lower()
                folder = rule.get("folder", "")
                if pattern and (pattern in class_name or pattern in label):
                    target_folder = folder
                    break

            if target_folder is None:
                target_folder = default_folder

            if target_folder is not None:
                try:
                    actor.set_folder_path(target_folder)
                    moved.append({
                        "actor": actor.get_name(),
                        "folder": target_folder,
                    })
                except Exception:
                    pass

    return {
        "status": "ok",
        "data": {
            "moved": len(moved),
            "actors": moved[:100],
        },
    }


@register_command("group_actors")
def group_actors(params: dict) -> dict:
    """Group actors under a shared folder path.

    Parameters
    ----------
    params.actor_names : list[str]
        Actor names or labels to group.
    params.group_name : str
        Folder path for the group (e.g., ``"Teams/TeamA"``).
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    actor_names = params["actor_names"]
    group_name = params["group_name"]

    actors = _find_actors_by_names(actor_names)
    if not actors:
        return {"status": "error", "error": "No matching actors found"}

    grouped = []

    with safe_transaction(f"WellVersed: Group into '{group_name}'"):
        for actor in actors:
            try:
                actor.set_folder_path(group_name)
                grouped.append(actor.get_name())
            except Exception:
                pass

    return {
        "status": "ok",
        "data": {
            "group": group_name,
            "grouped": len(grouped),
            "actor_names": grouped,
        },
    }


@register_command("tag_actors")
def tag_actors(params: dict) -> dict:
    """Add a gameplay tag to actors.

    Parameters
    ----------
    params.actor_names : list[str]
        Actor names or labels.
    params.tag : str
        Tag string to add (e.g., ``"TeamA"``, ``"Objective"``).
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    actor_names = params["actor_names"]
    tag = params["tag"]

    actors = _find_actors_by_names(actor_names)
    if not actors:
        return {"status": "error", "error": "No matching actors found"}

    tagged = []

    with safe_transaction(f"WellVersed: Tag actors with '{tag}'"):
        for actor in actors:
            try:
                current_tags = list(actor.tags) if hasattr(actor, "tags") else []
                tag_name = unreal.Name(tag)
                if tag_name not in current_tags:
                    current_tags.append(tag_name)
                    actor.tags = current_tags
                tagged.append(actor.get_name())
            except Exception:
                pass

    return {
        "status": "ok",
        "data": {
            "tag": tag,
            "tagged": len(tagged),
            "actor_names": tagged,
        },
    }


@register_command("find_actors_by_tag")
def find_actors_by_tag(params: dict) -> dict:
    """Find all actors that have a specific gameplay tag.

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
    results = []

    for actor in all_actors:
        try:
            if hasattr(actor, "tags"):
                if tag_name in actor.tags:
                    results.append(serialize_actor(actor))
        except Exception:
            pass

    return {
        "status": "ok",
        "data": {
            "tag": tag,
            "count": len(results),
            "actors": results,
        },
    }


@register_command("get_actor_hierarchy")
def get_actor_hierarchy(params: dict) -> dict:
    """Get the full folder tree of all actors in the level.

    Returns a nested dictionary representing the editor's folder hierarchy,
    with actor counts at each level and leaf actors listed.

    Parameters
    ----------
    params.include_actors : bool, optional
        Include actor details in leaf nodes. Default False (counts only).
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    include_actors = params.get("include_actors", False)
    all_actors = unreal.EditorLevelLibrary.get_all_level_actors()

    # Build folder tree
    tree: dict = {}
    root_actors = []

    for actor in all_actors:
        try:
            folder = actor.get_folder_path()
        except Exception:
            folder = ""

        if not folder:
            root_actors.append(actor)
            continue

        parts = folder.split("/")
        current = tree
        for part in parts:
            if part not in current:
                current[part] = {"_children": {}, "_actors": []}
            if "_children" not in current[part]:
                current[part] = {"_children": {}, "_actors": []}
            current[part]["_actors"].append(actor)
            current = current[part]["_children"]

    def _summarize(node: dict) -> dict:
        result = {}
        for key, value in node.items():
            if key.startswith("_"):
                continue
            actors = value.get("_actors", [])
            children = value.get("_children", {})
            entry = {
                "actor_count": len(actors),
            }
            if include_actors:
                entry["actors"] = [
                    {"name": a.get_name(), "label": a.get_actor_label(), "class": a.get_class().get_name()}
                    for a in actors
                ]
            if children:
                entry["children"] = _summarize(children)
            result[key] = entry
        return result

    hierarchy = _summarize(tree)
    hierarchy["_root"] = {
        "actor_count": len(root_actors),
    }
    if include_actors:
        hierarchy["_root"]["actors"] = [
            {"name": a.get_name(), "label": a.get_actor_label(), "class": a.get_class().get_name()}
            for a in root_actors[:100]
        ]

    return {
        "status": "ok",
        "data": {
            "total_actors": len(all_actors),
            "hierarchy": hierarchy,
        },
    }
