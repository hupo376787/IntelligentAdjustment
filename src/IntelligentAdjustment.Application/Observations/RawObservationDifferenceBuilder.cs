using IntelligentAdjustment.Core.Observations;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.Application.Observations;

public static class RawObservationDifferenceBuilder
{
    public static bool TryBuild(
        RawObservation observation,
        long levelDifferenceId,
        out LevelDifference? difference,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(observation);

        difference = null;
        error = null;

        if (!observation.IsValid)
        {
            error = observation.InvalidReason ?? "该原始观测已标记为无效。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(observation.FromPoint) ||
            string.IsNullOrWhiteSpace(observation.ToPoint))
        {
            error = "起点或终点为空。";
            return false;
        }

        if (string.Equals(observation.FromPoint, observation.ToPoint, StringComparison.Ordinal))
        {
            error = "起点和终点不能相同。";
            return false;
        }

        double heightDifference;
        double distanceMeters;

        bool hasDoubleReadings =
            observation.B1 is not null &&
            observation.B2 is not null &&
            observation.F1 is not null &&
            observation.F2 is not null;

        bool hasDoubleDistances =
            observation.DistanceB1 is not null &&
            observation.DistanceB2 is not null &&
            observation.DistanceF1 is not null &&
            observation.DistanceF2 is not null;

        if (hasDoubleReadings && hasDoubleDistances)
        {
            (heightDifference, distanceMeters) = LevelDifferenceBuilder.FromDoubleRun(
                observation.B1!.Value,
                observation.B2!.Value,
                observation.F1!.Value,
                observation.F2!.Value,
                observation.DistanceB1!.Value,
                observation.DistanceB2!.Value,
                observation.DistanceF1!.Value,
                observation.DistanceF2!.Value);
        }
        else if (observation.B1 is not null &&
                 observation.F1 is not null &&
                 observation.DistanceB1 is not null &&
                 observation.DistanceF1 is not null)
        {
            heightDifference = observation.B1.Value - observation.F1.Value;
            distanceMeters = observation.DistanceB1.Value + observation.DistanceF1.Value;
        }
        else
        {
            error = "读数或距离字段不完整，无法生成高差。";
            return false;
        }

        if (!double.IsFinite(heightDifference) ||
            !double.IsFinite(distanceMeters) ||
            distanceMeters <= 0)
        {
            error = "由原始观测计算出的高差或距离无效。";
            return false;
        }

        difference = new LevelDifference(
            levelDifferenceId,
            observation.LineId,
            observation.Sequence,
            observation.FromPoint.Trim(),
            observation.ToPoint.Trim(),
            heightDifference,
            distanceMeters,
            StationCount: 1,
            PointRole.AdjustmentPoint,
            IsRoleManuallySpecified: false,
            observation.Comment);

        return true;
    }
}
