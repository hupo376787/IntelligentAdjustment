namespace IntelligentAdjustment.Domain;

public sealed record ProjectSettings
{
    public ClosureToleranceMode ToleranceMode { get; init; } = ClosureToleranceMode.Distance;
    public double DistanceToleranceCoefficientMm { get; init; } = 4.0;
    public double StationToleranceCoefficientMm { get; init; } = 0.3;
    public AdjustmentMethod AdjustmentMethod { get; init; } = AdjustmentMethod.Classical;
    public bool AutoMergeTransitionPoints { get; init; }
    public bool AutoUpdateLevelDifferences { get; init; } = true;
    public int DistanceDecimals { get; init; } = 4;
    public int HeightDecimals { get; init; } = 5;
}

public sealed record LevelDifference(
    long Id,
    long LineId,
    int Sequence,
    string FromPoint,
    string ToPoint,
    double HeightDifference,
    double DistanceMeters,
    int StationCount,
    PointRole ToPointRole = PointRole.AdjustmentPoint,
    bool IsRoleManuallySpecified = false,
    string? Comment = null)
{
    public double DistanceKilometers => DistanceMeters / 1000.0;
}

public sealed record KnownHeight(string PointName, double Height, string? Comment = null);

public sealed record RawObservation
{
    public long Id { get; init; }
    public long LineId { get; init; }
    public int Sequence { get; init; }
    public string FromPoint { get; init; } = string.Empty;
    public string ToPoint { get; init; } = string.Empty;
    public double? B1 { get; init; }
    public double? B2 { get; init; }
    public double? F1 { get; init; }
    public double? F2 { get; init; }
    public double? DistanceB1 { get; init; }
    public double? DistanceB2 { get; init; }
    public double? DistanceF1 { get; init; }
    public double? DistanceF2 { get; init; }
    public MeasurementMode MeasurementMode { get; init; }
    public ObservationOrder ObservationOrder { get; init; }
    public bool IsValid { get; init; } = true;
    public string? InvalidReason { get; init; }
    public DateTimeOffset? MeasuredAt { get; init; }
    public double? TemperatureCelsius { get; init; }
    public string? SourceFileName { get; init; }
    public string? Comment { get; init; }
}

public sealed record RouteEdge(
    long ObservationId,
    string FromPoint,
    string ToPoint,
    double SignedHeightDifference,
    double DistanceMeters,
    int StationCount);

public sealed record NetworkRoute(
    int Index,
    RouteType RouteType,
    IReadOnlyList<string> Points,
    IReadOnlyList<RouteEdge> Edges,
    double ClosureMeters,
    double LengthMeters,
    int StationCount,
    double LengthToleranceMeters,
    double StationToleranceMeters)
{
    public int EdgeCount => Edges.Count;
}

public sealed record AdjustedHeight(
    string PointName,
    double Height,
    double StandardError,
    bool IsReferencePoint);

public sealed record AdjustedDifference(
    long ObservationId,
    string FromPoint,
    string ToPoint,
    double ObservedDifference,
    double AdjustedDifferenceValue,
    double Residual,
    double DistanceMeters,
    int StationCount,
    double StandardError);

public sealed record AdjustmentResult(
    AdjustmentMethod Method,
    IReadOnlyList<AdjustedHeight> Heights,
    IReadOnlyList<AdjustedDifference> Differences,
    double UnitWeightStandardDeviation,
    int DegreesOfFreedom,
    int Iterations,
    bool Converged,
    double? QuasiStableLambda = null);
