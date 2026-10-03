using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.Core.Observations;

public static class LevelDifferenceBuilder
{
    public static (double HeightDifference, double DistanceMeters) FromDoubleRun(
        double b1,
        double b2,
        double f1,
        double f2,
        double distanceB1,
        double distanceB2,
        double distanceF1,
        double distanceF2)
    {
        double heightDifference = (b1 + b2 - f1 - f2) / 2.0;
        double distanceMeters = (distanceB1 + distanceB2 + distanceF1 + distanceF2) / 2.0;
        return (heightDifference, distanceMeters);
    }
}
