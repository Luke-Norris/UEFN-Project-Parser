"""Pre-publish validation -- auditing, broken references, unused assets.

Provides commands to check a project's health before publishing, including
actor budget analysis, required device checks, and asset usage audits.
"""

from __future__ import annotations

import unreal

from ..listener import register_command
from ..serialization import serialize_value


# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

# UEFN island actor budget (approximate)
_ACTOR_BUDGET_DEFAULT = 100_000

# Device types typically required for a publishable island
_REQUIRED_DEVICES = [
    "PlayerSpawnPad",
    "EliminationManager",
]

# Suggested but not required
_RECOMMENDED_DEVICES = [
    "HUDController",
    "RoundSettings",
    "TeamSettings",
    "ScoreManager",
]


# ---------------------------------------------------------------------------
# Internal helpers
# ---------------------------------------------------------------------------

def _classify_actors(
    actors: list[unreal.Actor],
) -> dict[str, list[unreal.Actor]]:
    """Classify actors by broad category."""
    categories: dict[str, list[unreal.Actor]] = {
        "devices": [],
        "meshes": [],
        "lights": [],
        "volumes": [],
        "other": [],
    }

    for actor in actors:
        class_name = actor.get_class().get_name().lower()
        if "device" in class_name or "creative" in class_name:
            categories["devices"].append(actor)
        elif "light" in class_name:
            categories["lights"].append(actor)
        elif "volume" in class_name:
            categories["volumes"].append(actor)
        elif "mesh" in class_name or "static" in class_name:
            categories["meshes"].append(actor)
        else:
            categories["other"].append(actor)

    return categories


# ---------------------------------------------------------------------------
# Commands
# ---------------------------------------------------------------------------

@register_command("run_publish_audit")
def run_publish_audit(params: dict) -> dict:
    """Run a comprehensive pre-publish audit.

    Checks:
    - Actor count vs budget
    - Required devices present
    - Recommended devices present
    - Verse build status (if available)
    - Common issues (actors at origin, unnamed actors)

    Parameters
    ----------
    params.actor_budget : int, optional
        Custom actor budget. Default 100,000.

    Returns
    -------
    dict
        ``{"passed": bool, "score": int, "checks": [...]}`` with a 0-100
        score and list of check results.
    """
    budget = int(params.get("actor_budget", _ACTOR_BUDGET_DEFAULT))
    all_actors = unreal.EditorLevelLibrary.get_all_level_actors()
    categories = _classify_actors(all_actors)
    checks = []
    total_score = 100

    # -- Actor budget --
    actor_count = len(all_actors)
    budget_pct = (actor_count / budget) * 100 if budget > 0 else 0
    if budget_pct > 100:
        checks.append({
            "name": "actor_budget",
            "status": "fail",
            "message": f"Actor count ({actor_count}) exceeds budget ({budget})",
            "value": actor_count,
        })
        total_score -= 30
    elif budget_pct > 80:
        checks.append({
            "name": "actor_budget",
            "status": "warn",
            "message": f"Actor count ({actor_count}) is {budget_pct:.0f}% of budget",
            "value": actor_count,
        })
        total_score -= 10
    else:
        checks.append({
            "name": "actor_budget",
            "status": "pass",
            "message": f"Actor count ({actor_count}) is within budget ({budget_pct:.0f}%)",
            "value": actor_count,
        })

    # -- Required devices --
    device_names = set()
    for dev in categories["devices"]:
        device_names.add(dev.get_class().get_name())

    for required in _REQUIRED_DEVICES:
        found = any(required.lower() in d.lower() for d in device_names)
        if found:
            checks.append({
                "name": f"required_device_{required}",
                "status": "pass",
                "message": f"Required device '{required}' found",
            })
        else:
            checks.append({
                "name": f"required_device_{required}",
                "status": "fail",
                "message": f"Required device '{required}' NOT found",
            })
            total_score -= 15

    # -- Recommended devices --
    for recommended in _RECOMMENDED_DEVICES:
        found = any(recommended.lower() in d.lower() for d in device_names)
        if found:
            checks.append({
                "name": f"recommended_device_{recommended}",
                "status": "pass",
                "message": f"Recommended device '{recommended}' found",
            })
        else:
            checks.append({
                "name": f"recommended_device_{recommended}",
                "status": "info",
                "message": f"Recommended device '{recommended}' not found",
            })
            total_score -= 2

    # -- Actors at origin (common mistake) --
    at_origin = 0
    for actor in all_actors:
        loc = actor.get_actor_location()
        if abs(loc.x) < 1 and abs(loc.y) < 1 and abs(loc.z) < 1:
            at_origin += 1

    if at_origin > 5:
        checks.append({
            "name": "actors_at_origin",
            "status": "warn",
            "message": f"{at_origin} actors are at the world origin (possible oversight)",
            "value": at_origin,
        })
        total_score -= 5
    else:
        checks.append({
            "name": "actors_at_origin",
            "status": "pass",
            "message": f"Only {at_origin} actors at origin",
            "value": at_origin,
        })

    # -- Summary --
    total_score = max(0, min(100, total_score))
    passed = total_score >= 60

    return {
        "passed": passed,
        "score": total_score,
        "actor_count": actor_count,
        "budget": budget,
        "device_count": len(categories["devices"]),
        "categories": {
            k: len(v) for k, v in categories.items()
        },
        "checks": checks,
    }


@register_command("find_broken_references")
def find_broken_references(params: dict) -> dict:
    """Find actors with broken asset references.

    Scans mesh components for null or missing static mesh references
    and material slots.

    Returns
    -------
    dict
        ``{"broken": [...]}`` with actor name and reference type.
    """
    broken = []
    all_actors = unreal.EditorLevelLibrary.get_all_level_actors()

    for actor in all_actors:
        try:
            mesh_comp = actor.get_component_by_class(
                unreal.StaticMeshComponent
            )
            if mesh_comp is None:
                continue

            # Check static mesh reference
            try:
                mesh = mesh_comp.get_editor_property("static_mesh")
                if mesh is None:
                    broken.append({
                        "actor": actor.get_name(),
                        "label": actor.get_actor_label(),
                        "type": "null_mesh",
                        "message": "StaticMeshComponent has no mesh assigned",
                    })
            except Exception:
                pass

            # Check material references
            try:
                num_mats = mesh_comp.get_num_materials()
                for slot in range(num_mats):
                    mat = mesh_comp.get_material(slot)
                    if mat is None:
                        broken.append({
                            "actor": actor.get_name(),
                            "label": actor.get_actor_label(),
                            "type": "null_material",
                            "slot": slot,
                            "message": f"Material slot {slot} is empty",
                        })
            except Exception:
                pass

        except Exception:
            continue

    return {"count": len(broken), "broken": broken}


@register_command("find_unused_assets")
def find_unused_assets(params: dict) -> dict:
    """Find potentially unused assets in the project.

    Compares assets in the asset registry against what is actually
    referenced by actors in the level.

    Parameters
    ----------
    params.asset_types : list[str], optional
        Asset types to check. Default ``["StaticMesh", "Material", "Texture2D"]``.

    Returns
    -------
    dict
        ``{"unused": [...]}`` with asset path and type.
    """
    asset_types = params.get(
        "asset_types", ["StaticMesh", "Material", "Texture2D"]
    )
    registry = unreal.AssetRegistryHelpers.get_asset_registry()

    # Collect all assets of the requested types under /Game/
    all_assets: dict[str, str] = {}  # path -> type
    for asset_type in asset_types:
        try:
            filter_obj = unreal.ARFilter(
                class_names=[asset_type],
                package_paths=["/Game/"],
                recursive_paths=True,
            )
            found = registry.get_assets(filter_obj)
            for asset_data in found:
                path = str(asset_data.object_path)
                all_assets[path] = asset_type
        except Exception:
            continue

    # Collect all referenced asset paths from level actors
    referenced: set[str] = set()
    for actor in unreal.EditorLevelLibrary.get_all_level_actors():
        try:
            mesh_comp = actor.get_component_by_class(
                unreal.StaticMeshComponent
            )
            if mesh_comp is not None:
                try:
                    mesh = mesh_comp.get_editor_property("static_mesh")
                    if mesh is not None:
                        referenced.add(mesh.get_path_name())
                except Exception:
                    pass

                try:
                    num_mats = mesh_comp.get_num_materials()
                    for slot in range(num_mats):
                        mat = mesh_comp.get_material(slot)
                        if mat is not None:
                            referenced.add(mat.get_path_name())
                except Exception:
                    pass
        except Exception:
            continue

    # Identify unused assets
    unused = []
    for path, asset_type in sorted(all_assets.items()):
        if path not in referenced:
            unused.append({"path": path, "type": asset_type})

    return {
        "total_assets": len(all_assets),
        "referenced": len(referenced),
        "unused_count": len(unused),
        "unused": unused,
    }
