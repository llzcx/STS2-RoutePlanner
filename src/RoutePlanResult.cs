using System.Collections.Generic;
using MegaCrit.Sts2.Core.Map;

namespace RoutePlanner;

public class RoutePlanResult
{
    public List<MapPoint> BalancedRoute { get; set; } = new();
    public List<MapPoint> HighRewardRoute { get; set; } = new();
    public List<MapPoint> SafeRoute { get; set; } = new();
    public double BalancedScore { get; set; }
    public double HighRewardScore { get; set; }
    public double SafeScore { get; set; }
}
