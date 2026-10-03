namespace IntelligentAdjustment.Domain;

public enum AdjustmentMethod
{
    Classical = 0,
    QuasiStable = 1
}

public enum ClosureToleranceMode
{
    Distance = 0,
    StationCount = 1
}

public enum PointRole
{
    AdjustmentPoint = 0,
    TransitionPoint = 1
}

public enum RouteType
{
    Attached = 0,
    ClosedLoop = 1
}

public enum MeasurementMode
{
    Unknown = 0,
    BF = 1,
    BFFB = 2,
    AlternatingBF = 3,
    AlternatingBFFB = 4,
    BBFF = 5
}

public enum ObservationOrder
{
    Unknown = 0,
    BF,
    FB,
    BFFB,
    FBBF,
    BBFF,
    FFBB,
    BFBF,
    FBFB
}

public enum InstrumentVendor
{
    LeicaDna = 0,
    GeoMaxZdl = 1,
    SokkiaSdl = 2,
    TopconDl = 3,
    TrimbleDiNi = 4,
    LevNet = 5,
    GenericOut = 6
}
