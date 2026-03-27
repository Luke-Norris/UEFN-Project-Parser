"""Level intelligence -- world state export, device cataloging, level info.

These commands provide read-only snapshots of the current level state for
the WellVersed desktop application.
"""

from __future__ import annotations

import unreal

from ..listener import register_command
from ..serialization import serialize_actor, serialize_value


# ---------------------------------------------------------------------------
# Internal helpers
# ---------------------------------------------------------------------------

def _get_mesh_path(actor: unreal.Actor) -> str | None:
    """Try to extract the static mesh path from an actor's mesh component."""
    try:
        component = actor.get_component_by_class(
            unreal.StaticMeshComponent
        )
        if component is not None:
            mesh = component.get_editor_property("static_mesh")
            if mesh is not None:
                return mesh.get_path_name()
    except Exception:
        pass
    return None


def _get_readable_properties(actor: unreal.Actor) -> dict:
    """Read all readable editor properties, silently skipping failures."""
    result = {}
    try:
        for prop_name in actor.get_class().get_editor_property_names():
            prop_str = str(prop_name)
            try:
                val = actor.get_editor_property(prop_str)
                result[prop_str] = serialize_value(val)
            except Exception:
                pass
    except Exception:
        pass
    return result


# ---------------------------------------------------------------------------
# Commands
# ---------------------------------------------------------------------------

@register_command("export_world_state")
def export_world_state(params: dict) -> dict:
    """Export a complete snapshot of every actor in the current level.

    Parameters
    ----------
    params.include_properties : bool, optional
        Include all readable properties per actor. Default True.
    params.class_filter : str, optional
        Only include actors whose class name contains this substring.

    Returns
    -------
    dict
        ``{"count": int, "actors": [...]}`` where each actor entry
        contains name, class, transform, folder, tags, mesh path,
        and optionally all properties.
    """
    include_properties = params.get("include_properties", True)
    class_filter = params.get("class_filter", "")

    all_actors = unreal.EditorLevelLibrary.get_all_level_actors()
    results = []

    for actor in all_actors:
        class_name = actor.get_class().get_name()
        if class_filter and class_filter.lower() not in class_name.lower():
            continue

        data = serialize_actor(actor)
        data["mesh_path"] = _get_mesh_path(actor)

        if include_properties:
            data["properties"] = _get_readable_properties(actor)

        results.append(data)

    return {"count": len(results), "actors": results}


@register_command("device_catalog_scan")
def device_catalog_scan(params: dict) -> dict:
    """Scan the asset registry for available Creative device classes.

    Queries Blueprint assets and filters to those that appear to be
    Creative devices (class hierarchy heuristic).

    Parameters
    ----------
    params.search : str, optional
        Filter device names to those containing this substring.

    Returns
    -------
    dict
        ``{"count": int, "devices": [...]}`` with class path, display
        name, and category for each device.
    """
    search = params.get("search", "").lower()
    registry = unreal.AssetRegistryHelpers.get_asset_registry()

    # Search for Blueprint assets
    filter_obj = unreal.ARFilter(
        class_names=["Blueprint"],
        recursive_paths=True,
    )

    assets = registry.get_assets(filter_obj)
    devices = []

    # Heuristic: Creative devices tend to be in specific paths or have
    # certain naming patterns
    device_indicators = [
        "creative_device",
        "fortcreativedevice",
        "device",
        "creative",
    ]

    for asset_data in assets:
        asset_name = str(asset_data.asset_name).lower()
        package_path = str(asset_data.package_path).lower()

        # Check if this looks like a Creative device
        is_device = any(
            indicator in asset_name or indicator in package_path
            for indicator in device_indicators
        )

        if not is_device:
            continue

        display_name = str(asset_data.asset_name)
        if search and search not in display_name.lower():
            continue

        devices.append({
            "name": display_name,
            "path": str(asset_data.object_path),
            "package": str(asset_data.package_name),
        })

    # Sort by name
    devices.sort(key=lambda d: d["name"])

    return {"count": len(devices), "devices": devices}


@register_command("get_level_info")
def get_level_info(params: dict) -> dict:
    """Return summary information about the current level.

    Returns
    -------
    dict
        Level name, total actor count, device count, and rough bounds.
    """
    all_actors = unreal.EditorLevelLibrary.get_all_level_actors()

    # Compute bounding box from all actor locations
    min_x = min_y = min_z = float("inf")
    max_x = max_y = max_z = float("-inf")
    device_count = 0

    class_counts: dict[str, int] = {}

    for actor in all_actors:
        loc = actor.get_actor_location()
        min_x = min(min_x, loc.x)
        min_y = min(min_y, loc.y)
        min_z = min(min_z, loc.z)
        max_x = max(max_x, loc.x)
        max_y = max(max_y, loc.y)
        max_z = max(max_z, loc.z)

        class_name = actor.get_class().get_name()
        class_counts[class_name] = class_counts.get(class_name, 0) + 1

        # Heuristic: count Creative devices
        class_lower = class_name.lower()
        if "device" in class_lower or "creative" in class_lower:
            device_count += 1

    # Get level name
    world = unreal.EditorLevelLibrary.get_editor_world()
    level_name = world.get_name() if world else "Unknown"

    actor_count = len(all_actors)
    bounds = None
    if actor_count > 0:
        bounds = {
            "min": {"x": min_x, "y": min_y, "z": min_z},
            "max": {"x": max_x, "y": max_y, "z": max_z},
            "size": {
                "x": max_x - min_x,
                "y": max_y - min_y,
                "z": max_z - min_z,
            },
        }

    # Top classes by count
    top_classes = sorted(
        class_counts.items(), key=lambda kv: kv[1], reverse=True
    )[:20]

    return {
        "level_name": level_name,
        "actor_count": actor_count,
        "device_count": device_count,
        "bounds": bounds,
        "top_classes": [
            {"class": name, "count": count} for name, count in top_classes
        ],
    }
