# IntelligentAdjustment

IntelligentAdjustment is a .NET 8 WPF leveling-network adjustment application intended as a modern replacement for the legacy AdjustLevel workflow.

## Solution

- `IntelligentAdjustment.Domain` — domain models/enums, UI/database independent.
- `IntelligentAdjustment.Core` — classical/quasi-stable adjustment, closure/attached-route search, transition-point processing, tolerance calculation and observation formulas.
- `IntelligentAdjustment.Infrastructure` — single-file `.iap` SQLite project database.
- `IntelligentAdjustment.Application` — project session, import strategies and DOCX/XLSX/TXT reporting.
- `IntelligentAdjustment.App` — WPF + HandyControl desktop application.
- `IntelligentAdjustment.GoldenTests` — regression/golden tests reconstructed from legacy samples and black-box results.

## Project file

A user project is one file:

`<project name>.iap`

It is a SQLite database containing project metadata/settings, raw observations, level differences, known heights, network-sketch coordinates, report text, imported original instrument bytes, calculation runs, route results and adjustment results.

Reports and diagrams are exported artifacts and are not required beside the `.iap` file.

## Main application workflow

```text
New/Open .iap
  -> line management / instrument import / manual entry
  -> raw observations
  -> level differences + transition-point processing
  -> known heights
  -> network sketch
  -> closure/attached-route search
  -> classical or quasi-stable adjustment
  -> network graph / results
  -> DOCX / XLSX / TXT / PNG / SVG export
```

## Supported import strategies

The user explicitly selects the instrument/vendor format; no heuristic auto-detector is required.

- Leica DNA — GSI / MDT
- GeoMax ZDL — MDT
- Sokkia SDL — CSV
- Topcon DL — DAT
- Trimble DiNi — DAT, including the bundled sample's Measurement repeated / Station repeated semantics
- Lev_Net — OUT / MDT
- Generic level-difference OUT

Multiple selected files default to one observation line per file. Imported original bytes and SHA-256 are persisted in the project.

## WPF functionality

- traditional Menu + Toolbar with HandyControl styling
- modern left navigation + central tab workspace
- editable raw/level-difference/known-height grids
- undo/redo, multi-row operations, validation, clipboard support for level differences
- raw-observation profile view
- explicit transition-point roles and all verified merge/delete operations
- stale-result warning without deleting old results
- line management
- interactive network sketch:
  - manual placement
  - node drag
  - Ctrl multi-select
  - marquee selection
  - middle-mouse / Space+left pan
  - wheel zoom around cursor
  - Fit to View
  - coordinate delete
  - double-click reposition
  - optional snapping
  - synchronized visual scale bar
- network graph generated from sketch coordinates + topology
- graph/table selection linkage
- over-limit route highlighting
- PNG/SVG export

## Reporting

Reports do not use Office Interop.

- DOCX adjustment report
- XLSX result workbook
- UTF-8 TXT result export
- report chapter/table structure follows the supplied legacy `Report.doc` organization while standards/conclusions remain editable project content
- the application does not automatically claim inspection/acceptance conclusions that cannot be verified from adjustment data alone

## Frozen core formulas

### Classical adjustment

- weight: `p = 1 / S(km)`
- known heights are fixed
- degrees of freedom: `r = m - n + k`
- `sigma0 = sqrt(V'PV / r)`
- point error: `mH = sigma0 * sqrt(qii)`
- difference error: `mdH = sigma0 * sqrt(qii + qjj - 2*qij)`
- residual sign: `v = adjustedDifference - observedDifference`

### Quasi-stable adjustment

- stable/reference points are the points entered in the known-height list
- datum constraint: sum of stable-point height corrections equals zero
- `lambda = trace(N) / (5*n)`
- `Nq = N + lambda * C*C'`
- covariance/cofactor matrix: `Q = inverse(Nq)`
- stopping condition: `||dH||2 <= 0.0001 m`
- maximum iterations: `1000`
- degrees of freedom remains `m - n + k`, matching the verified legacy behavior

### Transition-point compression

For a continuous chain between adjustment points:

- `heightDifference = sum(heightDifference_i)`
- `distance = sum(distance_i)`
- `stationCount = sum(stationCount_i)`

Known-height points are protected from automatic transition-point recognition, including numeric point names.

### Route tolerances

- distance: `coefficient_mm * sqrt(length_km)`
- stations: `coefficient_mm * sqrt(stationCount)`
- both are calculated; the project setting selects which one determines over-limit status

## Build / test

Open `IntelligentAdjustment.sln` with Visual Studio 2022 17.14+ and restore NuGet packages.

```powershell
dotnet restore IntelligentAdjustment.sln
dotnet build IntelligentAdjustment.sln
dotnet test tests/IntelligentAdjustment.GoldenTests/IntelligentAdjustment.GoldenTests.csproj
```

GitHub Actions is intentionally configured as **manual-only** (`workflow_dispatch`) during active development so ordinary pushes do not start remote workflows.
