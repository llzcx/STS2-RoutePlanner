using System.Collections.Generic;
using MegaCrit.Sts2.Core.Map;

namespace RoutePlanner;

public class RoutePlanResult
{
    public List<MapPoint> BalancedRoute { get; set; } = new();
    public List<MapPoint> HighRewardRoute { get; set; } = new();
    public List<MapPoint> SafeRoute { get; set; } = new();
    public List<MapPoint> PriorityRoute { get; set; } = new();
    public double BalancedScore { get; set; }
    public double HighRewardScore { get; set; }
    public double SafeScore { get; set; }
    public double PriorityScore { get; set; }
    public bool BalancedConstraintsSatisfied { get; set; } = true;
    public bool HighRewardConstraintsSatisfied { get; set; } = true;
    public bool SafeConstraintsSatisfied { get; set; } = true;
    public bool PriorityConstraintsSatisfied { get; set; } = true;
}
