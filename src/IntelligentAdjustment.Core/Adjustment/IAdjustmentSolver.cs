using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.Core.Adjustment;

public interface IAdjustmentSolver
{
    AdjustmentResult Solve(
        IReadOnlyList<LevelDifference> observations,
        IReadOnlyList<KnownHeight> knownHeights);
}
