# Verified golden baselines

## Four-point network

Observations, all 100 m / 1 station:

- A -> B = +1.00000
- B -> C = +1.00000
- C -> D = +1.00000
- D -> A = -3.00200

### Classical, A=100.00000, C=102.01000

- A = 100.00000, error 0
- B = 101.00500, error 0.004527692569...
- C = 102.01000, error 0
- D = 103.00600, error 0.004527692569...
- residuals = +0.005, +0.005, -0.004, -0.004
- sigma0 = 0.020248456731...

### Quasi-stable, stable A=100.00000, C=102.01000

- lambda = 4
- A = 100.00450
- B = 101.00500
- C = 102.00550
- D = 103.00600
- dA + dC = 0
- residuals = +0.00050 each
- sigma0 = 0.002236067977...
- errors: A/C = 0.000661437828..., B/D = 0.00075

## Leica GSI closed loop

Merged network edges:

- ZHD -> I350 = +0.10815, 296.5545 m, 4 stations
- I350 -> I351 = -0.96835, 257.1199 m, 4 stations
- I351 -> I352 = +1.69395, 277.0288 m, 4 stations
- I352 -> I353 = -0.72565, 533.9245 m, 8 stations
- I353 -> ZHD = -0.10840, 296.5348 m, 4 stations

Known ZHD=3.50000.

Verified classical result:

- I350 = 3.60820355668094
- I351 = 2.63989999161431
- I352 = 4.33390002202373
- I353 = 3.60834644687681
- sigma0 = 0.000232763669636806

## Transition-point chain

Input:

- A -> 1 = +0.10000, 100 m
- 1 -> 2 = +0.20000, 110 m
- 2 -> B = +0.30000, 120 m
- B -> 3 = -0.10000, 130 m
- 3 -> C = +0.50000, 140 m
- C -> D = +0.70000, 150 m

With 1,2,3 as transition points, compressed output is:

- A -> B = +0.60000, 330 m, 3 stations
- B -> C = +0.40000, 270 m, 2 stations
- C -> D = +0.70000, 150 m, 1 station
