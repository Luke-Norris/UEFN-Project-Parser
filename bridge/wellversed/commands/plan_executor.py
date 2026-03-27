"""GenerationPlan executor -- the crown jewel.

Accepts a structured GenerationPlan JSON and executes it as a single undo
transaction in the editor. The plan describes devices to spawn, properties
to set, verse code to deploy, and a build trigger, all in one atomic
operation.

Plan Format::

    {
        "name": "My Game Mode",
        "description": "A simple elimination game",
        "steps": [
            {
                "type": "spawn",
                "class_path": "/Game/Devices/BP_EliminationManager",
                "label": "Elim_Manager",
                "location": {"x": 0, "y": 0, "z": 100},
                "properties": {"round_count": 3}
            },
            {
                "type": "set_properties",
                "target": "Elim_Manager",
                "properties": {"time_limit": 300}
            },
            {
                "type": "verse",
                "filename": "game_logic.verse",
                "content": "using { /Fortnite.com/... }\\n..."
            },
            {
                "type": "build"
            }
        ]
    }
"""

from __future__ import annotations

import time

import unreal

from ..listener import register_command
from ..safety import safe_transaction
from ..serialization import (
    serialize_actor,
    deserialize_vector,
    deserialize_rotator,
    coerce_property_value,
)


# ---------------------------------------------------------------------------
# Internal helpers
# ---------------------------------------------------------------------------

def _find_actor_by_label(label: str) -> unreal.Actor | None:
    """Find an actor by its editor label."""
    for actor in unreal.EditorLevelLibrary.get_all_level_actors():
        if actor.get_actor_label() == label:
            return actor
    return None


# ---------------------------------------------------------------------------
# Step executors
# ---------------------------------------------------------------------------

def _exec_spawn(step: dict, context: dict) -> dict:
    """Execute a spawn step."""
    class_path = step["class_path"]
    location = deserialize_vector(step.get("location", {}))
    rotation = deserialize_rotator(step.get("rotation", {}))
    label = step.get("label", "")

    asset = unreal.EditorAssetLibrary.load_asset(class_path)
    if asset is not None:
        actor = unreal.EditorLevelLibrary.spawn_actor_from_object(
            asset, location, rotation
        )
    else:
        actor_class = unreal.EditorAssetLibrary.load_blueprint_class(
            class_path
        )
        if actor_class is None:
            return {"status": "error", "error": f"Cannot load: {class_path}"}
        actor = unreal.EditorLevelLibrary.spawn_actor_from_class(
            actor_class, location, rotation
        )

    if actor is None:
        return {"status": "error", "error": f"Failed to spawn: {class_path}"}

    if label:
        actor.set_actor_label(label)

    # Set properties
    for prop_name, prop_value in step.get("properties", {}).items():
        try:
            coerced = coerce_property_value(prop_value)
            actor.set_editor_property(prop_name, coerced)
        except Exception as exc:
            unreal.log_warning(
                f"[WellVersed] Plan: could not set {prop_name}: {exc}"
            )

    # Track spawned actor by label for later reference
    if label:
        context["actors"][label] = actor

    return {
        "status": "ok",
        "actor": serialize_actor(actor),
    }


def _exec_set_properties(step: dict, context: dict) -> dict:
    """Execute a set_properties step on an existing actor."""
    target = step["target"]
    properties = step.get("properties", {})
    type_hints = step.get("type_hints", {})

    # Look up in context first (actors spawned in this plan), then in level
    actor = context["actors"].get(target)
    if actor is None:
        actor = _find_actor_by_label(target)
    if actor is None:
        return {"status": "error", "error": f"Actor not found: {target}"}

    results = {}
    for prop_name, prop_value in properties.items():
        try:
            hint = type_hints.get(prop_name)
            coerced = coerce_property_value(prop_value, hint)
            actor.set_editor_property(prop_name, coerced)
            results[prop_name] = "ok"
        except Exception as exc:
            results[prop_name] = f"error: {exc}"

    return {"status": "ok", "target": target, "results": results}


def _exec_delete(step: dict, context: dict) -> dict:
    """Execute a delete step."""
    target = step["target"]
    actor = context["actors"].get(target)
    if actor is None:
        actor = _find_actor_by_label(target)
    if actor is None:
        return {"status": "error", "error": f"Actor not found: {target}"}

    try:
        subsystem = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
        subsystem.destroy_actor(actor)
        context["actors"].pop(target, None)
        return {"status": "ok", "deleted": target}
    except Exception as exc:
        return {"status": "error", "error": str(exc)}


def _exec_duplicate(step: dict, context: dict) -> dict:
    """Execute a duplicate step."""
    source = step["source"]
    label = step.get("label", "")
    offset = deserialize_vector(step.get("offset", {}))

    actor = context["actors"].get(source)
    if actor is None:
        actor = _find_actor_by_label(source)
    if actor is None:
        return {"status": "error", "error": f"Actor not found: {source}"}

    subsystem = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
    new_actor = subsystem.duplicate_actor(actor)
    if new_actor is None:
        return {"status": "error", "error": "Duplication failed"}

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

    if label:
        new_actor.set_actor_label(label)
        context["actors"][label] = new_actor

    return {"status": "ok", "actor": serialize_actor(new_actor)}


def _exec_verse(step: dict, context: dict) -> dict:
    """Execute a verse file deployment step."""
    from .verse import write_verse_file
    return write_verse_file({
        "filename": step["filename"],
        "content": step["content"],
        "subdirectory": step.get("subdirectory", ""),
        "overwrite": step.get("overwrite", True),
    })


def _exec_build(step: dict, context: dict) -> dict:
    """Execute a build trigger step."""
    from .verse import trigger_build
    return trigger_build({})


def _exec_select(step: dict, context: dict) -> dict:
    """Execute an actor selection step."""
    names = step.get("names", [])
    actors = []
    for name in names:
        actor = context["actors"].get(name)
        if actor is None:
            actor = _find_actor_by_label(name)
        if actor is not None:
            actors.append(actor)

    unreal.EditorLevelLibrary.set_selected_level_actors(actors)
    return {
        "status": "ok",
        "selected": len(actors),
    }


# Step type -> executor mapping
_STEP_EXECUTORS = {
    "spawn": _exec_spawn,
    "set_properties": _exec_set_properties,
    "delete": _exec_delete,
    "duplicate": _exec_duplicate,
    "verse": _exec_verse,
    "build": _exec_build,
    "select": _exec_select,
}


# ---------------------------------------------------------------------------
# Command
# ---------------------------------------------------------------------------

@register_command("execute_generation_plan")
def execute_generation_plan(params: dict) -> dict:
    """Execute a full GenerationPlan as a single undo transaction.

    Parameters
    ----------
    params.plan : dict
        The generation plan with ``name``, ``description``, and ``steps``.
    params.dry_run : bool, optional
        If True, validate the plan without executing. Default False.

    Returns
    -------
    dict
        ``{"success": bool, "steps_completed": int, "results": [...]}``
        with per-step results.
    """
    plan = params["plan"]
    dry_run = params.get("dry_run", False)

    plan_name = plan.get("name", "Unnamed Plan")
    steps = plan.get("steps", [])

    if not steps:
        return {
            "success": True,
            "plan_name": plan_name,
            "steps_completed": 0,
            "results": [],
            "message": "Plan has no steps",
        }

    # Validate step types
    if dry_run:
        validation = []
        for i, step in enumerate(steps):
            step_type = step.get("type", "unknown")
            if step_type in _STEP_EXECUTORS:
                validation.append({
                    "step": i,
                    "type": step_type,
                    "valid": True,
                })
            else:
                validation.append({
                    "step": i,
                    "type": step_type,
                    "valid": False,
                    "error": f"Unknown step type: {step_type}",
                })

        all_valid = all(v["valid"] for v in validation)
        return {
            "success": all_valid,
            "plan_name": plan_name,
            "dry_run": True,
            "step_count": len(steps),
            "validation": validation,
        }

    # Execute the plan
    context = {"actors": {}}  # label -> actor mapping
    results = []
    steps_completed = 0
    start_time = time.monotonic()

    with safe_transaction(f"WellVersed: Plan '{plan_name}'"):
        for i, step in enumerate(steps):
            step_type = step.get("type", "unknown")
            executor = _STEP_EXECUTORS.get(step_type)

            if executor is None:
                results.append({
                    "step": i,
                    "type": step_type,
                    "status": "error",
                    "error": f"Unknown step type: {step_type}",
                })
                continue

            try:
                result = executor(step, context)
                result["step"] = i
                result["type"] = step_type
                results.append(result)

                if result.get("status") != "error":
                    steps_completed += 1
                else:
                    unreal.log_warning(
                        f"[WellVersed] Plan step {i} ({step_type}) "
                        f"failed: {result.get('error')}"
                    )
            except Exception as exc:
                results.append({
                    "step": i,
                    "type": step_type,
                    "status": "error",
                    "error": str(exc),
                })
                unreal.log_warning(
                    f"[WellVersed] Plan step {i} ({step_type}) "
                    f"exception: {exc}"
                )

    elapsed = time.monotonic() - start_time

    unreal.log(
        f"[WellVersed] Plan '{plan_name}' complete: "
        f"{steps_completed}/{len(steps)} steps in {elapsed:.2f}s"
    )

    return {
        "success": steps_completed == len(steps),
        "plan_name": plan_name,
        "steps_total": len(steps),
        "steps_completed": steps_completed,
        "elapsed_seconds": round(elapsed, 2),
        "results": results,
    }
