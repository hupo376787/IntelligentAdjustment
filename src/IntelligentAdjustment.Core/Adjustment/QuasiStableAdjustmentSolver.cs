using IntelligentAdjustment.Core.LinearAlgebra;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.Core.Adjustment;

public sealed class QuasiStableAdjustmentSolver : IAdjustmentSolver
{
    public const double ConvergenceToleranceMeters = 0.0001;
    public const int MaximumIterations = 1000;

    public AdjustmentResult Solve(
        IReadOnlyList<LevelDifference> observations,
        IReadOnlyList<KnownHeight> knownHeights)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(knownHeights);

        AdjustmentMath.ValidateObservations(observations);
        AdjustmentMath.EnsureConnected(observations);

        var points = AdjustmentMath.CollectNetworkPoints(observations);
        var pointIndex = points
            .Select((name, index) => (name, index))
            .ToDictionary(x => x.name, x => x.index, StringComparer.Ordinal);

        var stableMap = knownHeights
            .Where(x => pointIndex.ContainsKey(x.PointName))
            .GroupBy(x => x.PointName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Single().Height, StringComparer.Ordinal);

        if (stableMap.Count == 0)
        {
            throw new InvalidNetworkException("Quasi-stable adjustment requires at least one stable reference point in the network.");
        }

        int m = observations.Count;
        int n = points.Count;
        int k = stableMap.Count;
        int degreesOfFreedom = m - n + k;
        if (degreesOfFreedom <= 0)
        {
            throw new InvalidNetworkException("The adjustment has no positive redundancy (m - n + k <= 0).");
        }

        double[,] normal = BuildFreeNetworkNormal(observations, pointIndex, n);
        double trace = 0;
        for (int i = 0; i < n; i++)
        {
            trace += normal[i, i];
        }

        double lambda = trace / (5.0 * n);
        if (lambda <= 0 || !double.IsFinite(lambda))
        {
            throw new InvalidNetworkException("Unable to build the quasi-stable datum constraint.");
        }

        var c = new double[n];
        foreach (string point in stableMap.Keys)
        {
            c[pointIndex[point]] = 1;
        }

        var constrainedNormal = (double[,])normal.Clone();
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                constrainedNormal[i, j] += lambda * c[i] * c[j];
            }
        }

        double[,] q;
        try
        {
            q = DenseLinearAlgebra.Invert(constrainedNormal);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidNetworkException($"Quasi-stable adjustment failed: {ex.Message}");
        }

        double[] heights = BuildInitialHeights(observations, points, pointIndex, stableMap);
        bool converged = false;
        int iterations = 0;

        for (int iteration = 1; iteration <= MaximumIterations; iteration++)
        {
            var rhs = new double[n];
            foreach (var observation in observations)
            {
                double weight = AdjustmentMath.Weight(observation);
                int from = pointIndex[observation.FromPoint];
                int to = pointIndex[observation.ToPoint];
                double misclosure = observation.HeightDifference - (heights[to] - heights[from]);

                rhs[from] -= weight * misclosure;
                rhs[to] += weight * misclosure;
            }

            double[] correction;
            try
            {
                correction = DenseLinearAlgebra.Solve(constrainedNormal, rhs);
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidNetworkException($"Quasi-stable adjustment failed: {ex.Message}");
            }

            for (int i = 0; i < n; i++)
            {
                heights[i] += correction[i];
            }

            iterations = iteration;
            if (DenseLinearAlgebra.Norm2(correction) <= ConvergenceToleranceMeters)
            {
                converged = true;
                break;
            }
        }

        if (!converged)
        {
            throw new AdjustmentException($"Quasi-stable adjustment did not converge within {MaximumIterations} iterations.");
        }

        var residuals = new double[m];
        double vpv = 0;
        for (int i = 0; i < observations.Count; i++)
        {
            var observation = observations[i];
            int from = pointIndex[observation.FromPoint];
            int to = pointIndex[observation.ToPoint];
            residuals[i] = heights[to] - heights[from] - observation.HeightDifference;
            vpv += AdjustmentMath.Weight(observation) * residuals[i] * residuals[i];
        }

        double sigma0 = Math.Sqrt(vpv / degreesOfFreedom);

        var adjustedHeights = new List<AdjustedHeight>(n);
        for (int i = 0; i < n; i++)
        {
            adjustedHeights.Add(new AdjustedHeight(
                points[i],
                heights[i],
                sigma0 * Math.Sqrt(Math.Max(0, q[i, i])),
                stableMap.ContainsKey(points[i])));
        }

        var adjustedDifferences = new List<AdjustedDifference>(m);
        for (int i = 0; i < observations.Count; i++)
        {
            var observation = observations[i];
            int from = pointIndex[observation.FromPoint];
            int to = pointIndex[observation.ToPoint];
            double varianceFactor = q[from, from] + q[to, to] - 2 * q[from, to];
            double adjusted = heights[to] - heights[from];

            adjustedDifferences.Add(new AdjustedDifference(
                observation.Id,
                observation.FromPoint,
                observation.ToPoint,
                observation.HeightDifference,
                adjusted,
                residuals[i],
                observation.DistanceMeters,
                observation.StationCount,
                sigma0 * Math.Sqrt(Math.Max(0, varianceFactor))));
        }

        return new AdjustmentResult(
            AdjustmentMethod.QuasiStable,
            adjustedHeights,
            adjustedDifferences,
            sigma0,
            degreesOfFreedom,
            iterations,
            converged,
            lambda);
    }

    private static double[,] BuildFreeNetworkNormal(
        IReadOnlyList<LevelDifference> observations,
        IReadOnlyDictionary<string, int> pointIndex,
        int n)
    {
        var normal = new double[n, n];
        foreach (var observation in observations)
        {
            int from = pointIndex[observation.FromPoint];
            int to = pointIndex[observation.ToPoint];
            double weight = AdjustmentMath.Weight(observation);

            normal[from, from] += weight;
            normal[to, to] += weight;
            normal[from, to] -= weight;
            normal[to, from] -= weight;
        }

        return normal;
    }

    private static double[] BuildInitialHeights(
        IReadOnlyList<LevelDifference> observations,
        IReadOnlyList<string> points,
        IReadOnlyDictionary<string, int> pointIndex,
        IReadOnlyDictionary<string, double> stableMap)
    {
        var heights = Enumerable.Repeat(double.NaN, points.Count).ToArray();
        foreach (var pair in stableMap)
        {
            heights[pointIndex[pair.Key]] = pair.Value;
        }

        var adjacency = new Dictionary<string, List<(string Next, double Delta)>>(StringComparer.Ordinal);
        foreach (var point in points)
        {
            adjacency[point] = new List<(string Next, double Delta)>();
        }

        foreach (var observation in observations)
        {
            adjacency[observation.FromPoint].Add((observation.ToPoint, observation.HeightDifference));
            adjacency[observation.ToPoint].Add((observation.FromPoint, -observation.HeightDifference));
        }

        var queue = new Queue<string>(stableMap.Keys.OrderBy(x => x, StringComparer.Ordinal));
        var queued = new HashSet<string>(queue, StringComparer.Ordinal);

        while (queue.Count > 0)
        {
            string point = queue.Dequeue();
            int pointIdx = pointIndex[point];
            foreach (var (next, delta) in adjacency[point])
            {
                int nextIdx = pointIndex[next];
                if (double.IsNaN(heights[nextIdx]))
                {
                    heights[nextIdx] = heights[pointIdx] + delta;
                    if (queued.Add(next))
                    {
                        queue.Enqueue(next);
                    }
                }
            }
        }

        if (heights.Any(double.IsNaN))
        {
            throw new InvalidNetworkException("Unable to initialize all point heights from the stable points.");
        }

        return heights;
    }
}
