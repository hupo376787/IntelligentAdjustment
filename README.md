# IntelligentAdjustment

IntelligentAdjustment is a .NET 8 leveling-network adjustment application. This repository currently contains the verified calculation/data foundation; the WPF/HandyControl UI and instrument parsers will be added on top of these stable contracts.

## Current solution

- `IntelligentAdjustment.Domain` — domain models and enums; no UI/database dependency.
- `IntelligentAdjustment.Core` — classical adjustment, quasi-stable adjustment, closure/attached-route search, transition-point processing, tolerance calculation, observation formulas.
- `IntelligentAdjustment.Infrastructure` — single-file `.iap` SQLite project database and schema.
- `IntelligentAdjustment.GoldenTests` — verified regression tests reconstructed from AdjustLevel/LevDll and black-box projects.

## Project file

The user-facing project is one file:

`<project name>.iap`

Internally it is a SQLite database. Raw imported instrument bytes can be stored in `ImportSource.OriginalData`, so a project can remain self-contained. Reports/images are exported artifacts and are not required beside the `.iap` file.

SQLite is configured with `journal_mode=DELETE` and connection pooling disabled so a closed project file can be moved, renamed, or deleted immediately.

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
- degrees of freedom remains `m - n + k`, matching verified AdjustLevel behavior

### Transition-point compression

For a continuous chain between adjustment points:

- `heightDifference = sum(heightDifference_i)`
- `distance = sum(distance_i)`
- `stationCount = sum(stationCount_i)`

Known-height points are protected from automatic transition-point recognition.

### Route tolerances

- distance: `coefficient_mm * sqrt(length_km)`
- stations: `coefficient_mm * sqrt(stationCount)`
- both are calculated; the project setting only selects which one determines over-limit status

## Golden-test coverage

Current tests cover:

- verified four-point classical network
- verified Leica GSI closed-loop classical adjustment
- quasi-stable A/C datum, lambda and covariance behavior
- distance scaling of quasi-stable normal equations
- stable-point centroid constraint
- duplicate-edge two-edge loop
- square + diagonal short-cycle basis
- shared-edge loops
- dead-tail exclusion
- attached-route split at intermediate known point
- Y-shaped attached network redundancy filtering
- two independent paths between the same known endpoints
- attached-route closure sign
- distance/station tolerance mode
- automatic transition-point recognition and known-point protection
- transition-point compression
- selected-chain merge and delete
- BFFB normalized reading formula
- new `.iap` default SQLite schema/settings
- known-height points may exist before their point appears in the network

## Build / test

Open `IntelligentAdjustment.sln` with Visual Studio 2022 17.14+ and restore NuGet packages, then run Test Explorer or:

```powershell
dotnet test IntelligentAdjustment.sln
```

Core baseline validation status: **20/20 Golden Tests passing** on VS2022/.NET 8.

## Next implementation stages

1. Add WPF application project and HandyControl shell.
2. Add `.out` importer.
3. Add instrument parser projects/strategies (Leica, GeoMax, Trimble, Sokkia, Topcon).
4. Add project repositories, undo/redo command stack, stale-result revision handling.
5. Add graph/map UI and DOCX/XLSX report/export modules.
