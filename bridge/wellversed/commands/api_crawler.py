"""
API Capability Crawler — discovers what the UEFN Python API actually exposes.

Run this FIRST when the bridge connects. It maps the entire unreal.* surface
so WellVersed knows exactly what commands are available in this UEFN version.

Inspired by UEFN Toolbelt's api_capability_crawler.py but purpose-built
for WellVersed's needs.
"""

from ..listener import register_command

try:
    import unreal
except ImportError:
    unreal = None


def _is_valid(obj):
    """Check if an unreal object is valid and accessible."""
    try:
        if obj is None:
            return False
        if hasattr(obj, 'is_valid') and callable(obj.is_valid):
            return obj.is_valid()
        return True
    except:
        return False


def _safe_dir(obj):
    """Get dir() without crashing on broken objects."""
    try:
        return [x for x in dir(obj) if not x.startswith('__')]
    except:
        return []


def _get_subsystem_methods(subsystem_class_name):
    """Get all public methods from an editor subsystem."""
    methods = []
    try:
        sub = unreal.get_editor_subsystem(getattr(unreal, subsystem_class_name))
        if sub and _is_valid(sub):
            for name in _safe_dir(sub):
                if name.startswith('_'):
                    continue
                try:
                    attr = getattr(sub, name)
                    if callable(attr):
                        doc = getattr(attr, '__doc__', '') or ''
                        methods.append({
                            "name": name,
                            "doc": doc[:200] if doc else "",
                            "callable": True
                        })
                except:
                    pass
    except:
        pass
    return methods


@register_command("crawl_api_surface")
def crawl_api_surface(params):
    """
    Discover the full Python API surface available in this UEFN version.
    Maps subsystems, classes, methods, and capabilities.
    Returns a structured report of what's available.
    """
    report = {
        "unreal_version": "",
        "subsystems": {},
        "key_classes": {},
        "available_factories": [],
        "total_types": 0,
        "total_methods": 0,
        "capabilities": {
            "can_spawn_actors": False,
            "can_modify_properties": False,
            "can_create_materials": False,
            "can_create_assets": False,
            "can_simulate_pie": False,
            "can_control_viewport": False,
            "can_access_asset_registry": False,
            "can_use_transactions": False,
            "can_register_tick_callback": False,
        }
    }

    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    # Version info
    try:
        report["unreal_version"] = str(unreal.SystemLibrary.get_engine_version())
    except:
        report["unreal_version"] = "unknown"

    # Count total available types
    try:
        all_types = [x for x in dir(unreal) if not x.startswith('_')]
        report["total_types"] = len(all_types)
    except:
        pass

    # Check key subsystems
    subsystem_names = [
        "EditorActorSubsystem",
        "EditorAssetSubsystem",
        "LevelEditorSubsystem",
        "EditorUtilitySubsystem",
    ]
    for name in subsystem_names:
        methods = _get_subsystem_methods(name)
        if methods:
            report["subsystems"][name] = {
                "available": True,
                "method_count": len(methods),
                "methods": [m["name"] for m in methods]
            }
            report["total_methods"] += len(methods)
        else:
            report["subsystems"][name] = {"available": False, "method_count": 0, "methods": []}

    # Check key library classes (static method collections)
    library_classes = [
        "EditorLevelLibrary",
        "EditorAssetLibrary",
        "EditorFilterLibrary",
        "MaterialEditingLibrary",
        "AssetRegistryHelpers",
        "GameplayStatics",
        "KismetMathLibrary",
        "KismetSystemLibrary",
    ]
    for name in library_classes:
        try:
            cls = getattr(unreal, name, None)
            if cls:
                methods = [x for x in _safe_dir(cls) if not x.startswith('_') and callable(getattr(cls, x, None))]
                report["key_classes"][name] = {
                    "available": True,
                    "method_count": len(methods),
                    "methods": methods[:50]  # Cap at 50 to avoid huge responses
                }
                report["total_methods"] += len(methods)
            else:
                report["key_classes"][name] = {"available": False, "method_count": 0, "methods": []}
        except:
            report["key_classes"][name] = {"available": False, "method_count": 0, "methods": []}

    # Check asset factories (what types of assets can we create?)
    try:
        factory_types = [x for x in dir(unreal) if 'Factory' in x and not x.startswith('_')]
        report["available_factories"] = factory_types[:30]
    except:
        pass

    # Test capabilities
    try:
        # Can spawn actors?
        report["capabilities"]["can_spawn_actors"] = hasattr(unreal, 'EditorLevelLibrary') and hasattr(unreal.EditorLevelLibrary, 'spawn_actor_from_class')
    except:
        pass

    try:
        # Can modify properties?
        report["capabilities"]["can_modify_properties"] = True  # get/set_editor_property is universal
    except:
        pass

    try:
        # Can create materials?
        report["capabilities"]["can_create_materials"] = hasattr(unreal, 'MaterialEditingLibrary')
    except:
        pass

    try:
        # Can create assets?
        report["capabilities"]["can_create_assets"] = hasattr(unreal, 'AssetToolsHelpers') or hasattr(unreal, 'AssetTools')
    except:
        pass

    try:
        # Can simulate PIE?
        report["capabilities"]["can_simulate_pie"] = hasattr(unreal, 'EditorLevelLibrary') and hasattr(unreal.EditorLevelLibrary, 'editor_play_simulate')
    except:
        pass

    try:
        # Can control viewport?
        report["capabilities"]["can_control_viewport"] = hasattr(unreal, 'EditorLevelLibrary') and hasattr(unreal.EditorLevelLibrary, 'get_level_viewport_camera_info')
    except:
        pass

    try:
        # Can access asset registry?
        report["capabilities"]["can_access_asset_registry"] = hasattr(unreal, 'AssetRegistryHelpers')
    except:
        pass

    try:
        # Can use transactions?
        report["capabilities"]["can_use_transactions"] = hasattr(unreal, 'ScopedEditorTransaction')
    except:
        pass

    try:
        # Can register tick callbacks?
        report["capabilities"]["can_register_tick_callback"] = hasattr(unreal, 'register_slate_post_tick_callback')
    except:
        pass

    return {"status": "ok", "data": report}


@register_command("crawl_device_classes")
def crawl_device_classes(params):
    """
    Deep scan of all Creative device Blueprint classes available in this UEFN version.
    Uses the Asset Registry to find every placeable device.
    Returns categorized device list with class paths.
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    devices = []
    categories = {}

    try:
        registry = unreal.AssetRegistryHelpers.get_asset_registry()

        # Search for all Blueprint assets
        ar_filter = unreal.ARFilter()
        ar_filter.class_names = [unreal.Name("Blueprint"), unreal.Name("BlueprintGeneratedClass")]
        ar_filter.recursive_classes = True

        all_assets = registry.get_assets(ar_filter)

        for asset_data in all_assets:
            try:
                asset_name = str(asset_data.asset_name)
                package_path = str(asset_data.package_path) if hasattr(asset_data, 'package_path') else ""
                package_name = str(asset_data.package_name) if hasattr(asset_data, 'package_name') else ""

                # Filter to Creative devices
                is_device = False
                category = "Other"

                name_lower = asset_name.lower()
                if any(prefix in name_lower for prefix in ['bp_', 'pbwa_', 'b_']):
                    is_device = True

                if any(kw in name_lower for kw in ['device', 'spawner', 'trigger', 'timer', 'barrier',
                                                     'mutator', 'teleporter', 'granter', 'vending',
                                                     'elimination', 'score', 'hud', 'billboard',
                                                     'capture', 'zone', 'volume', 'button',
                                                     'camera', 'npc', 'guard', 'vehicle',
                                                     'prop', 'building', 'floor', 'wall', 'ramp']):
                    is_device = True

                if not is_device:
                    continue

                # Categorize
                if 'spawn' in name_lower:
                    category = "Spawning"
                elif 'trigger' in name_lower or 'button' in name_lower:
                    category = "Triggers"
                elif 'timer' in name_lower:
                    category = "Timing"
                elif 'score' in name_lower or 'elimination' in name_lower or 'tracker' in name_lower:
                    category = "Scoring"
                elif 'barrier' in name_lower or 'wall' in name_lower:
                    category = "Barriers"
                elif 'vending' in name_lower or 'granter' in name_lower:
                    category = "Economy"
                elif 'hud' in name_lower or 'billboard' in name_lower or 'message' in name_lower:
                    category = "UI"
                elif 'teleport' in name_lower:
                    category = "Movement"
                elif 'camera' in name_lower:
                    category = "Camera"
                elif 'npc' in name_lower or 'guard' in name_lower:
                    category = "NPCs"
                elif 'vehicle' in name_lower:
                    category = "Vehicles"
                elif 'mutator' in name_lower or 'zone' in name_lower or 'volume' in name_lower:
                    category = "Zones"
                elif 'capture' in name_lower:
                    category = "Objectives"
                elif 'prop' in name_lower or 'building' in name_lower or 'floor' in name_lower or 'ramp' in name_lower:
                    category = "Props"

                device_info = {
                    "name": asset_name,
                    "category": category,
                    "package": package_name or package_path,
                }
                devices.append(device_info)

                if category not in categories:
                    categories[category] = 0
                categories[category] += 1

            except:
                continue

    except Exception as e:
        return {"status": "error", "error": f"Asset Registry scan failed: {str(e)}"}

    return {
        "status": "ok",
        "data": {
            "total_devices": len(devices),
            "categories": categories,
            "devices": devices[:500],  # Cap response size
            "truncated": len(devices) > 500
        }
    }


@register_command("crawl_actor_classes")
def crawl_actor_classes(params):
    """
    List all actor classes that can be spawned via spawn_actor_from_class.
    Checks which unreal.* types are subclasses of Actor.
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    spawnable = []
    try:
        actor_base = unreal.Actor
        for name in dir(unreal):
            if name.startswith('_'):
                continue
            try:
                cls = getattr(unreal, name)
                if isinstance(cls, type) and issubclass(cls, actor_base) and cls is not actor_base:
                    spawnable.append(name)
            except:
                continue
    except Exception as e:
        return {"status": "error", "error": str(e)}

    return {
        "status": "ok",
        "data": {
            "total_spawnable": len(spawnable),
            "classes": sorted(spawnable)[:200]
        }
    }
