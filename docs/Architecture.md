# Architecture baseline

## Dependency direction

```text
WPF/UI
  -> Application (future)
      -> Core
      -> Domain
      -> Infrastructure

Instrument Parser(s)
  -> Domain RawObservation
  -> Core LevelDifferenceBuilder
  -> Core TransitionPointProcessor
  -> Core RouteSearchEngine
  -> Core Adjustment Solver
```

The UI must not contain adjustment mathematics. Database entities should not leak into the solver API.

## Core services

- `ClassicalAdjustmentSolver`
- `QuasiStableAdjustmentSolver`
- `RouteSearchEngine`
- `ClosureToleranceCalculator`
- `TransitionPointProcessor`
- `LevelDifferenceBuilder`

## Linear algebra

The baseline contains a small, dependency-free dense Gaussian-elimination backend so the formula implementation is explicit and testable. It is intentionally isolated under `Core/LinearAlgebra`.

For very large production networks, replace it behind a future linear-algebra abstraction with a proven sparse implementation without changing Domain or solver behavior. Do not reintroduce the original LevDll fixed-size 1000-point arrays.
