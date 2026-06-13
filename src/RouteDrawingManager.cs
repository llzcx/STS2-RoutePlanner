using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace RoutePlanner;

public class RouteDrawingManager
{
    private readonly NMapScreen _mapScreen;
    private Dictionary<(MapCoord, MapCoord), IReadOnlyList<TextureRect>>? _cachedPathsDict;
    private bool _pathsDictCached;

    public RouteDrawingManager(NMapScreen mapScreen)
    {
        _mapScreen = mapScreen;
    }

    public void DrawRoute(List<MapPoint> path, Color color)
    {
        if (path.Count < 2) return;

        var drawings = GetNMapDrawings();
        if (drawings == null) return;

        var allPositions = GetPathDotPositions(path, drawings);
        if (allPositions.Count < 2) return;

        var segments = BuildLineSegments(allPositions);
        foreach (var segment in segments)
        {
            drawings.BeginLineLocal(segment[0], DrawingMode.Drawing);
            for (int i = 1; i < segment.Length; i++)
            {
                drawings.UpdateCurrentLinePositionLocal(segment[i]);
            }
            drawings.StopLineLocal();
        }
    }

    public void ClearAllDrawings()
    {
        GetNMapDrawings()?.ClearDrawnLinesLocal();
    }

    private NMapDrawings? GetNMapDrawings()
    {
        var prop = typeof(NMapScreen).GetProperty("Drawings",
            BindingFlags.Public | BindingFlags.Instance);
        return prop?.GetValue(_mapScreen) as NMapDrawings;
    }

    /// <summary>
    /// Get all positions along the route, interpolated through the game's path dots
    /// for smooth curves that follow the map's connection lines.
    /// </summary>
    private List<Vector2> GetPathDotPositions(List<MapPoint> path, Control reference)
    {
        var allPositions = new List<Vector2>();

        // Cache the _paths dictionary lookup
        var pathsDict = GetPathsDictionary();
        var pointsDict = GetMapPointDictionary();

        if (pointsDict == null) return allPositions;

        // For each consecutive pair, use ONLY the game's path dots — no extra node centers
        for (int i = 1; i < path.Count; i++)
        {
            var fromPoint = path[i - 1];
            var toPoint = path[i];

            // Try to get the path dots for this edge
            bool hasDots = false;
            if (pathsDict != null)
            {
                var key = (fromPoint.coord, toPoint.coord);
                var reverseKey = (toPoint.coord, fromPoint.coord);

                IReadOnlyList<TextureRect>? dots = null;
                if (pathsDict.TryGetValue(key, out var d1))
                    dots = d1;
                else if (pathsDict.TryGetValue(reverseKey, out var d2))
                    dots = d2;

                if (dots != null && dots.Count > 0)
                {
                    hasDots = true;
                    var fromNodePos = GetNodePosition(fromPoint, pointsDict, reference);
                    var toNodePos = GetNodePosition(toPoint, pointsDict, reference);

                    // Order dots: project onto the from→to line for correct sequence
                    var dir = toNodePos - fromNodePos;
                    float segLen = dir.Length();
                    if (segLen > 0.01f)
                        dir /= segLen;

                    var sortedDots = dots
                        .Where(d => GodotObject.IsInstanceValid(d))
                        .Select(d => new { localPos = d.GlobalPosition - reference.GlobalPosition })
                        .Select(d => new { d.localPos, t = (d.localPos - fromNodePos).Dot(dir) })
                        .OrderBy(d => d.t)
                        .ToList();

                    foreach (var dot in sortedDots)
                        allPositions.Add(dot.localPos);
                }
            }

            if (!hasDots)
            {
                // Fallback: interpolate between the two nodes
                var fromNodePos = GetNodePosition(fromPoint, pointsDict, reference);
                var toNodePos = GetNodePosition(toPoint, pointsDict, reference);
                float dist = fromNodePos.DistanceTo(toNodePos);
                var dir = (toNodePos - fromNodePos).Normalized();
                int dotCount = (int)(dist / 22f);
                for (int d = 1; d <= dotCount; d++)
                {
                    allPositions.Add(fromNodePos + dir * (d * 22f));
                }
            }
        }

        // Drop leading dots that are behind the first node's edge (cosmetic trim)
        if (allPositions.Count > 0 && path.Count > 0)
        {
            var firstNodePos = GetNodePosition(path[0], pointsDict, reference);
            float trimThreshold = 10f;
            while (allPositions.Count > 1 && allPositions[0].DistanceSquaredTo(firstNodePos) < trimThreshold * trimThreshold)
                allPositions.RemoveAt(0);
        }

        return allPositions;
    }

    private Vector2 GetNodePosition(MapPoint point,
        Dictionary<MapCoord, NMapPoint> pointsDict, Control reference)
    {
        if (pointsDict.TryGetValue(point.coord, out var nPoint) && GodotObject.IsInstanceValid(nPoint))
        {
            return nPoint.GlobalPosition - reference.GlobalPosition;
        }
        // Fallback
        return new Vector2(40 + point.coord.col * 140, 40 + point.coord.row * 130);
    }

    private Dictionary<(MapCoord, MapCoord), IReadOnlyList<TextureRect>>? GetPathsDictionary()
    {
        if (_pathsDictCached)
            return _cachedPathsDict;

        _pathsDictCached = true;
        var field = typeof(NMapScreen).GetField("_paths",
            BindingFlags.NonPublic | BindingFlags.Instance);
        _cachedPathsDict = field?.GetValue(_mapScreen) as Dictionary<(MapCoord, MapCoord), IReadOnlyList<TextureRect>>;
        return _cachedPathsDict;
    }

    private Dictionary<MapCoord, NMapPoint>? GetMapPointDictionary()
    {
        var field = typeof(NMapScreen).GetField("_mapPointDictionary",
            BindingFlags.NonPublic | BindingFlags.Instance);
        return field?.GetValue(_mapScreen) as Dictionary<MapCoord, NMapPoint>;
    }

    /// <summary>
    /// Split positions into contiguous segments.
    /// A new segment starts when subsequent positions are not close to each other.
    /// </summary>
    private static Vector2[][] BuildLineSegments(List<Vector2> allPositions)
    {
        if (allPositions.Count < 2)
            return new Vector2[0][];

        var segments = new List<Vector2[]>();
        var currentSegment = new List<Vector2> { allPositions[0] };

        for (int i = 1; i < allPositions.Count; i++)
        {
            var prev = allPositions[i - 1];
            var cur = allPositions[i];
            float gap = prev.DistanceTo(cur);

            // If the gap is too large, this is a new segment (different node pair)
            if (gap > 50f)
            {
                if (currentSegment.Count > 1)
                    segments.Add(currentSegment.ToArray());
                currentSegment.Clear();
            }

            if (currentSegment.Count == 0)
                currentSegment.Add(prev);

            currentSegment.Add(cur);
        }

        if (currentSegment.Count > 1)
            segments.Add(currentSegment.ToArray());

        return segments.ToArray();
    }
}
