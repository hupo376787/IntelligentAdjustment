using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.Core.Routes;

public static class ClosureToleranceCalculator
{
    public static double ByDistanceMeters(double lengthMeters, double coefficientMm)
    {
        if (lengthMeters < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lengthMeters));
        }

        return coefficientMm * Math.Sqrt(lengthMeters / 1000.0) / 1000.0;
    }

    public static double ByStationMeters(int stationCount, double coefficientMm)
    {
        if (stationCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stationCount));
        }

        return coefficientMm * Math.Sqrt(stationCount) / 1000.0;
    }

    public static bool IsOverLimit(NetworkRoute route, ProjectSettings settings)
    {
        double active = settings.ToleranceMode == ClosureToleranceMode.Distance
            ? route.LengthToleranceMeters
            : route.StationToleranceMeters;

        return Math.Abs(route.ClosureMeters) > active;
    }
}
