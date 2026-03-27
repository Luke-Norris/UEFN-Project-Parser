"""
Spline-based prop placement — roads, paths, fences, walls along curves.

Inspired by UEFN Toolbelt's spline_prop_placer.py. Places actors at
regular intervals along a polyline path defined by control points.
Supports count mode (N items) and distance mode (every X units).
"""

import math
from ..listener import register_command
from ..safety import safe_transaction

try:
    import unreal
except ImportError:
    unreal = None


def _lerp_vec(a, b, t):
    """Linear interpolation between two (x,y,z) tuples."""
    return (
        a[0] + (b[0] - a[0]) * t,
        a[1] + (b[1] - a[1]) * t,
        a[2] + (b[2] - a[2]) * t,
    )


def _distance(a, b):
    """3D distance between two (x,y,z) tuples."""
    dx = b[0] - a[0]
    dy = b[1] - a[1]
    dz = b[2] - a[2]
    return math.sqrt(dx * dx + dy * dy + dz * dz)


def _direction_yaw(a, b):
    """Yaw angle (degrees) from point a to point b."""
    dx = b[0] - a[0]
    dy = b[1] - a[1]
    return math.degrees(math.atan2(dy, dx))


def _compute_path_length(points):
    """Total length of a polyline path."""
    total = 0.0
    for i in range(len(points) - 1):
        total += _distance(points[i], points[i + 1])
    return total


def _sample_path_by_count(points, count):
    """Sample N evenly-spaced points along the polyline."""
    if count <= 0:
        return []
    if count == 1:
        mid = len(points) // 2
        return [points[mid] if points else (0, 0, 0)]

    total_length = _compute_path_length(points)
    if total_length <= 0:
        return [points[0]] * count if points else []

    spacing = total_length / (count - 1)
    return _sample_path_by_distance(points, spacing)


def _sample_path_by_distance(points, spacing):
    """Sample points along polyline at regular distance intervals."""
    if not points or spacing <= 0:
        return []

    samples = [points[0]]
    accumulated = 0.0
    next_sample_at = spacing
    segment_idx = 0

    while segment_idx < len(points) - 1:
        seg_start = points[segment_idx]
        seg_end = points[segment_idx + 1]
        seg_len = _distance(seg_start, seg_end)

        if seg_len <= 0:
            segment_idx += 1
            continue

        remaining_in_segment = seg_len
        local_offset = 0.0

        while next_sample_at - accumulated <= remaining_in_segment + 0.001:
            t = (next_sample_at - accumulated) / seg_len
            t = max(0.0, min(1.0, t))
            sample = _lerp_vec(seg_start, seg_end, t)
            samples.append(sample)
            next_sample_at += spacing

        accumulated += seg_len
        segment_idx += 1

    return samples


@register_command("spline_place_by_count")
def spline_place_by_count(params):
    """
    Place N actors evenly along a polyline path.

    params:
        points: list of [x, y, z] control points defining the path
        count: number of actors to place
        actor_class: class path or name to spawn (e.g., "/Game/Props/SM_Fence_C")
        align_to_path: bool — rotate actors to face along the path direction (default True)
        scale: float — uniform scale (default 1.0)
        z_offset: float — height offset above the path (default 0)
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    points_raw = params.get("points", [])
    count = params.get("count", 10)
    actor_class = params.get("actor_class", "")
    align_to_path = params.get("align_to_path", True)
    scale_factor = params.get("scale", 1.0)
    z_offset = params.get("z_offset", 0.0)

    if not points_raw or len(points_raw) < 2:
        return {"status": "error", "error": "Need at least 2 control points"}
    if not actor_class:
        return {"status": "error", "error": "actor_class is required"}
    if count <= 0:
        return {"status": "error", "error": "count must be > 0"}

    points = [(p[0], p[1], p[2]) for p in points_raw]
    samples = _sample_path_by_count(points, count)

    return _place_along_samples(samples, points, actor_class, align_to_path, scale_factor, z_offset)


@register_command("spline_place_by_distance")
def spline_place_by_distance(params):
    """
    Place actors at regular distance intervals along a polyline path.

    params:
        points: list of [x, y, z] control points
        spacing: distance between actors (Unreal units, ~1cm each)
        actor_class: class path or name to spawn
        align_to_path: bool (default True)
        scale: float (default 1.0)
        z_offset: float (default 0)
    """
    if not unreal:
        return {"status": "error", "error": "unreal module not available"}

    points_raw = params.get("points", [])
    spacing = params.get("spacing", 200.0)
    actor_class = params.get("actor_class", "")
    align_to_path = params.get("align_to_path", True)
    scale_factor = params.get("scale", 1.0)
    z_offset = params.get("z_offset", 0.0)

    if not points_raw or len(points_raw) < 2:
        return {"status": "error", "error": "Need at least 2 control points"}
    if not actor_class:
        return {"status": "error", "error": "actor_class is required"}
    if spacing <= 0:
        return {"status": "error", "error": "spacing must be > 0"}

    points = [(p[0], p[1], p[2]) for p in points_raw]
    samples = _sample_path_by_distance(points, spacing)

    return _place_along_samples(samples, points, actor_class, align_to_path, scale_factor, z_offset)


@register_command("spline_preview")
def spline_preview(params):
    """
    Preview where actors would be placed along a path without actually spawning them.
    Returns the computed positions and rotations.

    params:
        points: list of [x, y, z] control points
        count: number of placements (use this OR spacing)
        spacing: distance between placements (use this OR count)
    """
    points_raw = params.get("points", [])
    count = params.get("count")
    spacing = params.get("spacing")

    if not points_raw or len(points_raw) < 2:
        return {"status": "error", "error": "Need at least 2 control points"}

    points = [(p[0], p[1], p[2]) for p in points_raw]

    if count and count > 0:
        samples = _sample_path_by_count(points, count)
    elif spacing and spacing > 0:
        samples = _sample_path_by_distance(points, spacing)
    else:
        return {"status": "error", "error": "Provide either 'count' or 'spacing'"}

    path_length = _compute_path_length(points)

    preview = []
    for i, pos in enumerate(samples):
        yaw = 0.0
        if i < len(samples) - 1:
            yaw = _direction_yaw(pos, samples[i + 1])
        elif i > 0:
            yaw = _direction_yaw(samples[i - 1], pos)

        preview.append({
            "index": i,
            "position": {"x": pos[0], "y": pos[1], "z": pos[2]},
            "yaw": yaw
        })

    return {
        "status": "ok",
        "data": {
            "placement_count": len(preview),
            "path_length": path_length,
            "placements": preview
        }
    }


def _place_along_samples(samples, path_points, actor_class, align_to_path, scale_factor, z_offset):
    """Place actors at sampled positions along a path."""
    # Load the asset/class
    asset = None
    cls = None
    try:
        asset = unreal.EditorAssetLibrary.load_asset(actor_class)
    except:
        pass

    if not asset:
        try:
            cls = getattr(unreal, actor_class, None)
        except:
            pass

    if not asset and not cls:
        return {"status": "error", "error": f"Could not find actor class: {actor_class}"}

    placed = []
    errors = []

    with safe_transaction(f"WellVersed: Spline Place {len(samples)} actors"):
        for i, pos in enumerate(samples):
            try:
                # Compute yaw rotation along path
                yaw = 0.0
                if align_to_path:
                    if i < len(samples) - 1:
                        yaw = _direction_yaw(pos, samples[i + 1])
                    elif i > 0:
                        yaw = _direction_yaw(samples[i - 1], pos)

                location = unreal.Vector(pos[0], pos[1], pos[2] + z_offset)
                rotation = unreal.Rotator(0, yaw, 0)

                if asset:
                    actor = unreal.EditorLevelLibrary.spawn_actor_from_object(
                        asset, location, rotation)
                else:
                    actor = unreal.EditorLevelLibrary.spawn_actor_from_class(
                        cls, location, rotation)

                if actor:
                    # Apply scale
                    if scale_factor != 1.0:
                        actor.set_actor_scale3d(unreal.Vector(scale_factor, scale_factor, scale_factor))

                    placed.append({
                        "index": i,
                        "name": actor.get_name(),
                        "position": {"x": pos[0], "y": pos[1], "z": pos[2] + z_offset},
                        "yaw": yaw
                    })
                else:
                    errors.append(f"Failed to spawn actor at index {i}")
            except Exception as e:
                errors.append(f"Error at index {i}: {str(e)}")

    return {
        "status": "ok" if placed else "error",
        "data": {
            "placed_count": len(placed),
            "error_count": len(errors),
            "path_length": _compute_path_length([(p[0], p[1], p[2]) for p in [samples[0], samples[-1]] if samples]),
            "actors": placed,
            "errors": errors[:10]
        }
    }
