using System;
using System.Collections.Generic;
using System.Numerics;

namespace PuppetMasterKK;

// Path shaping for walking with vnavmesh (pure, tested). vnavmesh's paths are straight lines between corners, so the
// character runs to a point, turns sharply, runs to the next. Rounding each corner into a short curve, and never
// cutting more than a little off it, makes the run look natural without swinging into walls.
internal static class PathSmoothing
{
    // How far before (and after) a corner the curve starts. Small, so the curve stays close to the original path.
    public const float CornerCut = 1.5f;
    // Points per rounded corner.
    private const int CornerSteps = 4;

    // Drops the waypoints at the start of a new path that are already behind us (we kept running while it was being
    // worked out), so swapping the path in never makes the character turn back.
    public static List<Vector3> TrimBehind(IReadOnlyList<Vector3> path, Vector3 position, Vector3 heading, float near = 1.5f)
    {
        var start = 0;
        var flatHeading = new Vector2(heading.X, heading.Z);
        var hasHeading = flatHeading.LengthSquared() > 0.0001f;
        // Never drop the last point (the destination).
        while (start < path.Count - 1)
        {
            var offset = path[start] - position;
            var flat = new Vector2(offset.X, offset.Z);
            var behind = hasHeading && Vector2.Dot(flat, flatHeading) < 0f;
            if (flat.Length() > near && !behind)
                break;
            start++;
        }
        var trimmed = new List<Vector3>(path.Count - start);
        for (var i = start; i < path.Count; i++)
            trimmed.Add(path[i]);
        return trimmed;
    }

    // Rounds every inner corner of from -> path[0] -> path[1] ... into a short curve. The result doesn't include
    // `from`; the last point is always the destination.
    public static List<Vector3> RoundCorners(Vector3 from, IReadOnlyList<Vector3> path)
    {
        var points = new List<Vector3>(path.Count + 1) { from };
        points.AddRange(path);
        var result = new List<Vector3>(path.Count * (CornerSteps + 1));
        for (var i = 1; i < points.Count; i++)
        {
            var corner = points[i];
            if (i == points.Count - 1)
            {
                result.Add(corner);
                break;
            }
            var before = points[i - 1];
            var after = points[i + 1];
            var inLength = Vector3.Distance(before, corner);
            var outLength = Vector3.Distance(corner, after);
            var cut = MathF.Min(CornerCut, MathF.Min(inLength, outLength) * 0.5f);
            if (cut < 0.05f)
            {
                result.Add(corner);
                continue;
            }
            var entry = corner + (before - corner) * (cut / inLength);
            var exit = corner + (after - corner) * (cut / outLength);
            // Quadratic curve from entry to exit, pulled toward the corner.
            for (var step = 0; step <= CornerSteps; step++)
            {
                var t = step / (float)CornerSteps;
                var u = 1f - t;
                result.Add(u * u * entry + 2f * u * t * corner + t * t * exit);
            }
        }
        return result;
    }
}
