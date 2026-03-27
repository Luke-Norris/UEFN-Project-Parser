"""Performance tools -- budget reports, overlap detection, optimization suggestions.

Provides commands for analyzing level performance characteristics: actor
budget tracking, overlapping actor detection, zero-scale actor detection,
memory estimation, and actionable optimization suggestions.
"""

from __future__ import annotations

import math

from ..listener import register_command

try:
    import unreal
    from ..serialization import serialize_actor
except ImportError:
    unreal = None


# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

# UEFN actor budget tiers
_BUDGET_TIERS = {
    "warning": 10_000,
    "critical": 15_000,
    "hard_limit": 20_000,
}

# Estimated memory cost per actor type (rough approximations in KB)
_MEMORY_ESTIMATES = {
    "StaticMeshActor": 2,
    "StaticMeshComponent": 1,
    "PointLight": 4,
    "SpotLight": 4,
    "DirectionalLight": 2,
    "SkeletalMeshActor": 50,
    "Landscape": 200,
    "ParticleSystem": 30,
    "Emitter": 30,
    "Niagara": 25,
    "Decal": 3,
    "default": 5,
}


# ---------------------------------------------------------------------------
# Internal helpers
# ---------------------------------------------------------------------------

def _distance_3d(a, b) -> float:
    """3D distance between two actor locations."""
    dx = a.x - b.x
    dy = a.y - b.y
    dz = a.z - b.z
    return math.sqrt(dx * dx + dy * dy + dz * dz)


def _estimate_actor_memory(class_name: str) -> float:
    """Estimate memory usage in KB for an actor class."""
    class_lower = class_name.lower()
    for key, kb in _MEMORY_ESTIMATES.items():
        if key.lower() in class_lower:
            return kb
    return _MEMORY_ESTIMATES["default"]


# ---------------------------------------------------------------------------
# Commands
# ---------------------------------------------------------------------------

@register_command("get_actor_budget_report")
def get_actor_budget_report(params: dict) -> dict:
    """Get actor count vs UEFN budget limits, broken down by type.

    Returns total actor count, percentage of each budget tier used,
    and a breakdown by actor class.

    Parameters
    ----------
    (no parameters required)
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    all_actors = unreal.EditorLevelLibrary.get_all_level_actors()
    total = len(all_actors)

    # Count by class
    class_counts: dict[str, int] = {}
    for actor in all_actors:
        class_name = actor.get_class().get_name()
        class_counts[class_name] = class_counts.get(class_name, 0) + 1

    # Sort by count descending
    sorted_classes = sorted(
        class_counts.items(), key=lambda kv: kv[1], reverse=True
    )

    # Budget analysis
    budget_status = "ok"
    if total >= _BUDGET_TIERS["hard_limit"]:
        budget_status = "over_limit"
    elif total >= _BUDGET_TIERS["critical"]:
        budget_status = "critical"
    elif total >= _BUDGET_TIERS["warning"]:
        budget_status = "warning"

    return {
        "status": "ok",
        "data": {
            "total_actors": total,
            "budget_status": budget_status,
            "budget_tiers": _BUDGET_TIERS,
            "budget_usage": {
                tier: f"{(total / limit) * 100:.1f}%"
                for tier, limit in _BUDGET_TIERS.items()
            },
            "remaining_to_warning": max(0, _BUDGET_TIERS["warning"] - total),
            "remaining_to_hard_limit": max(0, _BUDGET_TIERS["hard_limit"] - total),
            "top_classes": [
                {"class": name, "count": count, "percentage": f"{(count / total) * 100:.1f}%"}
                for name, count in sorted_classes[:30]
            ],
        },
    }


@register_command("find_overlapping_actors")
def find_overlapping_actors(params: dict) -> dict:
    """Detect actors at nearly identical positions (potential duplicates).

    Parameters
    ----------
    params.threshold : float, optional
        Maximum distance (Unreal units) to consider as overlapping.
        Default 5.0 (5 cm).
    params.class_filter : str, optional
        Only check actors of this class (substring match).
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    threshold = float(params.get("threshold", 5.0))
    class_filter = params.get("class_filter", "").lower()

    all_actors = unreal.EditorLevelLibrary.get_all_level_actors()

    # Filter actors
    filtered = []
    for actor in all_actors:
        if class_filter:
            class_name = actor.get_class().get_name().lower()
            if class_filter not in class_name:
                continue
        filtered.append(actor)

    # O(n^2) overlap detection -- limit to first 5000 actors for performance
    filtered = filtered[:5000]
    overlaps = []

    for i in range(len(filtered)):
        loc_i = filtered[i].get_actor_location()
        class_i = filtered[i].get_class().get_name()

        for j in range(i + 1, len(filtered)):
            loc_j = filtered[j].get_actor_location()

            if _distance_3d(loc_i, loc_j) <= threshold:
                # Only report if same class (likely duplicate)
                class_j = filtered[j].get_class().get_name()
                overlaps.append({
                    "actor_a": {
                        "name": filtered[i].get_name(),
                        "label": filtered[i].get_actor_label(),
                        "class": class_i,
                    },
                    "actor_b": {
                        "name": filtered[j].get_name(),
                        "label": filtered[j].get_actor_label(),
                        "class": class_j,
                    },
                    "distance": _distance_3d(loc_i, loc_j),
                    "same_class": class_i == class_j,
                    "location": {"x": loc_i.x, "y": loc_i.y, "z": loc_i.z},
                })

    return {
        "status": "ok",
        "data": {
            "overlap_count": len(overlaps),
            "threshold": threshold,
            "checked_actors": len(filtered),
            "overlaps": overlaps[:100],
        },
    }


@register_command("find_zero_scale_actors")
def find_zero_scale_actors(params: dict) -> dict:
    """Find actors with zero or near-zero scale (invisible but consuming budget).

    Parameters
    ----------
    params.scale_threshold : float, optional
        Actors with any scale axis below this value are flagged.
        Default 0.01.
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    threshold = float(params.get("scale_threshold", 0.01))

    all_actors = unreal.EditorLevelLibrary.get_all_level_actors()
    zero_scale = []

    for actor in all_actors:
        try:
            scale = actor.get_actor_scale3d()
            if (
                abs(scale.x) < threshold
                or abs(scale.y) < threshold
                or abs(scale.z) < threshold
            ):
                zero_scale.append({
                    "name": actor.get_name(),
                    "label": actor.get_actor_label(),
                    "class": actor.get_class().get_name(),
                    "scale": {"x": scale.x, "y": scale.y, "z": scale.z},
                })
        except Exception:
            pass

    return {
        "status": "ok",
        "data": {
            "count": len(zero_scale),
            "threshold": threshold,
            "actors": zero_scale[:100],
        },
    }


@register_command("get_memory_estimate")
def get_memory_estimate(params: dict) -> dict:
    """Estimate rough memory usage by actor type.

    Uses approximate per-actor memory costs to provide a ballpark
    estimate. Not exact, but useful for identifying heavy asset types.

    Parameters
    ----------
    (no parameters required)
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    all_actors = unreal.EditorLevelLibrary.get_all_level_actors()

    type_memory: dict[str, dict] = {}
    total_kb = 0.0

    for actor in all_actors:
        class_name = actor.get_class().get_name()
        kb = _estimate_actor_memory(class_name)
        total_kb += kb

        if class_name not in type_memory:
            type_memory[class_name] = {"count": 0, "total_kb": 0.0, "per_actor_kb": kb}
        type_memory[class_name]["count"] += 1
        type_memory[class_name]["total_kb"] += kb

    # Sort by total memory descending
    sorted_types = sorted(
        type_memory.items(),
        key=lambda kv: kv[1]["total_kb"],
        reverse=True,
    )

    return {
        "status": "ok",
        "data": {
            "total_estimated_mb": round(total_kb / 1024, 2),
            "total_actors": len(all_actors),
            "by_type": [
                {
                    "class": name,
                    "count": info["count"],
                    "estimated_mb": round(info["total_kb"] / 1024, 3),
                    "per_actor_kb": info["per_actor_kb"],
                }
                for name, info in sorted_types[:30]
            ],
        },
    }


@register_command("get_draw_call_estimate")
def get_draw_call_estimate(params: dict) -> dict:
    """Estimate draw call complexity based on mesh and material counts.

    Each unique material slot on a mesh actor typically generates one
    draw call. This scans visible mesh actors and reports the estimated
    draw call count.

    Parameters
    ----------
    (no parameters required)
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    all_actors = unreal.EditorLevelLibrary.get_all_level_actors()

    total_draw_calls = 0
    mesh_actors = 0
    unique_meshes: set[str] = set()
    unique_materials: set[str] = set()

    for actor in all_actors:
        try:
            comp = actor.get_component_by_class(unreal.StaticMeshComponent)
            if comp is None:
                continue

            mesh_actors += 1

            # Count material slots (each = ~1 draw call)
            try:
                num_mats = comp.get_num_materials()
                total_draw_calls += max(1, num_mats)

                for slot in range(num_mats):
                    try:
                        mat = comp.get_material(slot)
                        if mat is not None:
                            unique_materials.add(mat.get_path_name())
                    except Exception:
                        pass
            except Exception:
                total_draw_calls += 1

            # Track unique meshes
            try:
                mesh = comp.get_editor_property("static_mesh")
                if mesh is not None:
                    unique_meshes.add(mesh.get_path_name())
            except Exception:
                pass

        except Exception:
            pass

    return {
        "status": "ok",
        "data": {
            "estimated_draw_calls": total_draw_calls,
            "mesh_actors": mesh_actors,
            "unique_meshes": len(unique_meshes),
            "unique_materials": len(unique_materials),
            "avg_materials_per_mesh": round(total_draw_calls / max(mesh_actors, 1), 2),
        },
    }


@register_command("suggest_optimizations")
def suggest_optimizations(params: dict) -> dict:
    """Analyze the level and return actionable optimization suggestions.

    Runs multiple checks and returns prioritized suggestions for
    improving level performance.

    Parameters
    ----------
    (no parameters required)
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    all_actors = unreal.EditorLevelLibrary.get_all_level_actors()
    total = len(all_actors)

    suggestions = []
    priority_map = {"high": 1, "medium": 2, "low": 3}

    # 1. Actor budget check
    if total >= _BUDGET_TIERS["hard_limit"]:
        suggestions.append({
            "priority": "high",
            "category": "budget",
            "title": "Actor count exceeds hard limit",
            "detail": f"Level has {total} actors, exceeding the {_BUDGET_TIERS['hard_limit']} hard limit. "
                      f"Remove or merge actors to bring the count down.",
            "reduction_needed": total - _BUDGET_TIERS["hard_limit"],
        })
    elif total >= _BUDGET_TIERS["critical"]:
        suggestions.append({
            "priority": "high",
            "category": "budget",
            "title": "Actor count in critical range",
            "detail": f"Level has {total} actors, approaching the {_BUDGET_TIERS['hard_limit']} hard limit.",
        })
    elif total >= _BUDGET_TIERS["warning"]:
        suggestions.append({
            "priority": "medium",
            "category": "budget",
            "title": "Actor count above warning threshold",
            "detail": f"Level has {total} actors. Consider optimizing before adding more content.",
        })

    # 2. Zero-scale actors
    zero_count = 0
    for actor in all_actors:
        try:
            s = actor.get_actor_scale3d()
            if abs(s.x) < 0.01 or abs(s.y) < 0.01 or abs(s.z) < 0.01:
                zero_count += 1
        except Exception:
            pass

    if zero_count > 0:
        suggestions.append({
            "priority": "medium",
            "category": "cleanup",
            "title": f"{zero_count} zero-scale actors found",
            "detail": "These actors are invisible but consume budget. Use find_zero_scale_actors to list them.",
        })

    # 3. Duplicate position check (sample first 2000)
    sample = all_actors[:2000]
    close_pairs = 0
    for i in range(len(sample)):
        loc_i = sample[i].get_actor_location()
        for j in range(i + 1, min(i + 50, len(sample))):
            loc_j = sample[j].get_actor_location()
            if _distance_3d(loc_i, loc_j) < 5.0:
                if sample[i].get_class().get_name() == sample[j].get_class().get_name():
                    close_pairs += 1

    if close_pairs > 0:
        suggestions.append({
            "priority": "medium",
            "category": "duplicates",
            "title": f"~{close_pairs} potential duplicate actors detected",
            "detail": "Actors of the same class at the same position may be accidental duplicates. "
                      "Use find_overlapping_actors for the full list.",
        })

    # 4. Class diversity check
    class_counts: dict[str, int] = {}
    for actor in all_actors:
        cn = actor.get_class().get_name()
        class_counts[cn] = class_counts.get(cn, 0) + 1

    # Find dominant class
    if class_counts:
        top_class, top_count = max(class_counts.items(), key=lambda kv: kv[1])
        if top_count > total * 0.5 and top_count > 100:
            suggestions.append({
                "priority": "low",
                "category": "diversity",
                "title": f"'{top_class}' makes up {(top_count/total)*100:.0f}% of all actors",
                "detail": f"{top_count} actors of this type. Consider using instanced meshes or "
                          f"reducing count if they're decorative.",
            })

    # 5. Light count check
    light_count = sum(
        c for name, c in class_counts.items() if "light" in name.lower()
    )
    if light_count > 50:
        suggestions.append({
            "priority": "medium",
            "category": "lighting",
            "title": f"{light_count} light actors in the level",
            "detail": "Many dynamic lights can impact performance. Consider baking lighting or reducing light count.",
        })

    # Sort by priority
    suggestions.sort(key=lambda s: priority_map.get(s["priority"], 99))

    return {
        "status": "ok",
        "data": {
            "total_actors": total,
            "suggestion_count": len(suggestions),
            "suggestions": suggestions,
        },
    }
