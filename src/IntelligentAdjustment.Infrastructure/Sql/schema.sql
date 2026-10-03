PRAGMA foreign_keys = ON;
PRAGMA journal_mode = DELETE;
PRAGMA synchronous = FULL;

CREATE TABLE IF NOT EXISTS SchemaInfo (
    Id INTEGER PRIMARY KEY CHECK (Id = 1),
    Version INTEGER NOT NULL
);
INSERT OR IGNORE INTO SchemaInfo(Id, Version) VALUES (1, 1);

CREATE TABLE IF NOT EXISTS ProjectInfo (
    Id INTEGER PRIMARY KEY CHECK (Id = 1),
    ProjectName TEXT NOT NULL DEFAULT '',
    ProjectNumber TEXT NOT NULL DEFAULT '',
    UnitName TEXT NOT NULL DEFAULT '',
    ProjectLeader TEXT NOT NULL DEFAULT '',
    Reviewer TEXT NOT NULL DEFAULT '',
    CreatedAtUtc TEXT NOT NULL,
    UpdatedAtUtc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS ProjectSettings (
    Id INTEGER PRIMARY KEY CHECK (Id = 1),
    ToleranceMode INTEGER NOT NULL DEFAULT 0,
    DistanceToleranceCoefficientMm REAL NOT NULL DEFAULT 4.0,
    StationToleranceCoefficientMm REAL NOT NULL DEFAULT 0.3,
    AdjustmentMethod INTEGER NOT NULL DEFAULT 0,
    AutoMergeTransitionPoints INTEGER NOT NULL DEFAULT 0,
    AutoUpdateLevelDifferences INTEGER NOT NULL DEFAULT 1,
    DistanceDecimals INTEGER NOT NULL DEFAULT 4,
    HeightDecimals INTEGER NOT NULL DEFAULT 5,
    PointNameHorizontalAlignment INTEGER NOT NULL DEFAULT 1,
    PointNameVerticalAlignment INTEGER NOT NULL DEFAULT 0,
    OpenLastProjectOnStartup INTEGER NOT NULL DEFAULT 0
);
INSERT OR IGNORE INTO ProjectSettings(Id) VALUES (1);

CREATE TABLE IF NOT EXISTS ProjectState (
    Id INTEGER PRIMARY KEY CHECK (Id = 1),
    InputRevision INTEGER NOT NULL DEFAULT 0,
    ResultRevision INTEGER NOT NULL DEFAULT -1,
    LastCalculatedAtUtc TEXT NULL
);
INSERT OR IGNORE INTO ProjectState(Id) VALUES (1);

CREATE TABLE IF NOT EXISTS ObservationLine (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    DisplayOrder INTEGER NOT NULL,
    Name TEXT NOT NULL,
    SourceFileName TEXT NULL,
    InstrumentType TEXT NULL,
    CreatedAtUtc TEXT NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_ObservationLine_DisplayOrder ON ObservationLine(DisplayOrder);

CREATE TABLE IF NOT EXISTS ImportSource (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ObservationLineId INTEGER NULL REFERENCES ObservationLine(Id) ON DELETE SET NULL,
    FileName TEXT NOT NULL,
    InstrumentType TEXT NOT NULL,
    ImportedAtUtc TEXT NOT NULL,
    Sha256 TEXT NOT NULL,
    OriginalData BLOB NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_ImportSource_LineId ON ImportSource(ObservationLineId);

CREATE TABLE IF NOT EXISTS RawObservation (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ObservationLineId INTEGER NOT NULL REFERENCES ObservationLine(Id) ON DELETE CASCADE,
    Sequence INTEGER NOT NULL,
    FromPoint TEXT NOT NULL,
    ToPoint TEXT NOT NULL,
    B1 REAL NULL,
    B2 REAL NULL,
    F1 REAL NULL,
    F2 REAL NULL,
    DistanceB1 REAL NULL,
    DistanceB2 REAL NULL,
    DistanceF1 REAL NULL,
    DistanceF2 REAL NULL,
    MeasurementMode INTEGER NOT NULL DEFAULT 0,
    ObservationOrder INTEGER NOT NULL DEFAULT 0,
    IsValid INTEGER NOT NULL DEFAULT 1,
    InvalidReason TEXT NULL,
    MeasuredAtUtc TEXT NULL,
    TemperatureCelsius REAL NULL,
    Comment TEXT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_RawObservation_Line_Sequence ON RawObservation(ObservationLineId, Sequence);

CREATE TABLE IF NOT EXISTS LevelDifference (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ObservationLineId INTEGER NOT NULL REFERENCES ObservationLine(Id) ON DELETE CASCADE,
    Sequence INTEGER NOT NULL,
    FromPoint TEXT NOT NULL,
    ToPoint TEXT NOT NULL,
    HeightDifference REAL NOT NULL,
    DistanceMeters REAL NOT NULL,
    StationCount INTEGER NOT NULL,
    ToPointRole INTEGER NOT NULL DEFAULT 0,
    IsRoleManuallySpecified INTEGER NOT NULL DEFAULT 0,
    SourceRawStartSequence INTEGER NULL,
    SourceRawEndSequence INTEGER NULL,
    Comment TEXT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_LevelDifference_Line_Sequence ON LevelDifference(ObservationLineId, Sequence);
CREATE INDEX IF NOT EXISTS IX_LevelDifference_FromPoint ON LevelDifference(FromPoint);
CREATE INDEX IF NOT EXISTS IX_LevelDifference_ToPoint ON LevelDifference(ToPoint);

-- Deliberately no FK to network points: a known height may be entered before its point exists in the network.
CREATE TABLE IF NOT EXISTS KnownHeight (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    PointName TEXT NOT NULL,
    Height REAL NOT NULL,
    Comment TEXT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS UX_KnownHeight_PointName ON KnownHeight(PointName);

-- Logical canvas coordinates only; never used in adjustment mathematics.
CREATE TABLE IF NOT EXISTS NetworkMapPoint (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    PointName TEXT NOT NULL,
    X REAL NOT NULL,
    Y REAL NOT NULL,
    UpdatedAtUtc TEXT NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS UX_NetworkMapPoint_PointName ON NetworkMapPoint(PointName);

CREATE TABLE IF NOT EXISTS CalculationRun (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    InputRevision INTEGER NOT NULL,
    AdjustmentMethod INTEGER NOT NULL,
    ToleranceMode INTEGER NOT NULL,
    StartedAtUtc TEXT NOT NULL,
    CompletedAtUtc TEXT NULL,
    IsSuccessful INTEGER NOT NULL DEFAULT 0,
    ErrorMessage TEXT NULL,
    UnitWeightStandardDeviation REAL NULL,
    DegreesOfFreedom INTEGER NULL,
    Iterations INTEGER NULL,
    QuasiStableLambda REAL NULL
);

CREATE TABLE IF NOT EXISTS ClosureRoute (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CalculationRunId INTEGER NOT NULL REFERENCES CalculationRun(Id) ON DELETE CASCADE,
    RouteIndex INTEGER NOT NULL,
    RouteType INTEGER NOT NULL,
    EdgeCount INTEGER NOT NULL,
    LengthMeters REAL NOT NULL,
    StationCount INTEGER NOT NULL,
    ClosureMeters REAL NOT NULL,
    LengthToleranceMeters REAL NOT NULL,
    StationToleranceMeters REAL NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS UX_ClosureRoute_Run_Index ON ClosureRoute(CalculationRunId, RouteIndex);

CREATE TABLE IF NOT EXISTS ClosureRoutePoint (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ClosureRouteId INTEGER NOT NULL REFERENCES ClosureRoute(Id) ON DELETE CASCADE,
    Sequence INTEGER NOT NULL,
    PointName TEXT NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS UX_ClosureRoutePoint_Route_Sequence ON ClosureRoutePoint(ClosureRouteId, Sequence);

CREATE TABLE IF NOT EXISTS AdjustmentResultHeight (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CalculationRunId INTEGER NOT NULL REFERENCES CalculationRun(Id) ON DELETE CASCADE,
    PointName TEXT NOT NULL,
    Height REAL NOT NULL,
    StandardError REAL NOT NULL,
    IsReferencePoint INTEGER NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS UX_AdjustmentResultHeight_Run_Point ON AdjustmentResultHeight(CalculationRunId, PointName);

CREATE TABLE IF NOT EXISTS AdjustmentResultDifference (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CalculationRunId INTEGER NOT NULL REFERENCES CalculationRun(Id) ON DELETE CASCADE,
    LevelDifferenceId INTEGER NULL REFERENCES LevelDifference(Id) ON DELETE SET NULL,
    FromPoint TEXT NOT NULL,
    ToPoint TEXT NOT NULL,
    ObservedDifference REAL NOT NULL,
    AdjustedDifference REAL NOT NULL,
    Residual REAL NOT NULL,
    DistanceMeters REAL NOT NULL,
    StationCount INTEGER NOT NULL,
    StandardError REAL NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_AdjustmentResultDifference_Run ON AdjustmentResultDifference(CalculationRunId);

CREATE TABLE IF NOT EXISTS ReportText (
    Id INTEGER PRIMARY KEY CHECK (Id = 1),
    TaskOverview TEXT NOT NULL DEFAULT '',
    NaturalGeography TEXT NOT NULL DEFAULT '',
    ExistingData TEXT NOT NULL DEFAULT '',
    ReferencedStandards TEXT NOT NULL DEFAULT '',
    TechnicalIndicators TEXT NOT NULL DEFAULT '',
    FieldWorkSummary TEXT NOT NULL DEFAULT '',
    ConclusionAndRecommendations TEXT NOT NULL DEFAULT ''
);
INSERT OR IGNORE INTO ReportText(Id) VALUES (1);
