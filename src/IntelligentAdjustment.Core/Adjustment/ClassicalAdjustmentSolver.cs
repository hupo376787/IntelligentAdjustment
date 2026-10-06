using IntelligentAdjustment.Core.LinearAlgebra;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.Core.Adjustment;

public sealed class ClassicalAdjustmentSolver : IAdjustmentSolver
{
    public AdjustmentResult Solve(
        IReadOnlyList<LevelDifference> observations,
        IReadOnlyList<KnownHeight> knownHeights)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(knownHeights);

        AdjustmentMath.ValidateObservations(observations);
        AdjustmentMath.EnsureConnected(observations);

        var points = AdjustmentMath.CollectNetworkPoints(observations);
        var knownMap = knownHeights
            .GroupBy(x => x.PointName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Single().Height, StringComparer.Ordinal);

        var knownInNetwork = points.Where(knownMap.ContainsKey).ToArray();
        if (knownInNetwork.Length == 0)
        {
            throw new InvalidNetworkException("Classical adjustment requires at least one known-height point in the network.");
        }

        var unknownPoints = points.Where(p => !knownMap.ContainsKey(p)).ToArray();
        if (unknownPoints.Length == 0)
        {
            throw new InvalidNetworkException("The network has no unknown heights to adjust.");
        }

        int m = observations.Count;
        int n = points.Count;
        int k = knownInNetwork.Length;
        int degreesOfFreedom = m - n + k;
        if (degreesOfFreedom < 0)
        {
            throw new InvalidNetworkException("The adjustment has negative redundancy (m - n + k < 0).");
        }

        var unknownIndex = unknownPoints
            .Select((name, index) => (name, index))
            .ToDictionary(x => x.name, x => x.index, StringComparer.Ordinal);

        int u = unknownPoints.Length;
        var normal = new double[u, u];
        var rhs = new double[u];

        foreach (var observation in observations)
        {
            double weight = AdjustmentMath.Weight(observation);
            var row = new double[u];
            double knownContribution = 0;

            if (unknownIndex.TryGetValue(observation.FromPoint, out int fromIndex))
            {
                row[fromIndex] -= 1;
            }
            else
            {
                knownContribution -= knownMap[observation.FromPoint];
            }

            if (unknownIndex.TryGetValue(observation.ToPoint, out int toIndex))
            {
                row[toIndex] += 1;
            }
            else
            {
                knownContribution += knownMap[observation.ToPoint];
            }

            double target = observation.HeightDifference - knownContribution;
            AddNormalEquation(normal, rhs, row, target, weight);
        }

        double[] unknownHeights;
        double[,] qUnknown;
        try
        {
            unknownHeights = DenseLinearAlgebra.Solve(normal, rhs);
            qUnknown = DenseLinearAlgebra.Invert(normal);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidNetworkException($"Classical adjustment failed: {ex.Message}");
        }

        var finalHeights = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (string point in knownInNetwork)
        {
            finalHeights[point] = knownMap[point];
        }

        for (int i = 0; i < unknownPoints.Length; i++)
        {
            finalHeights[unknownPoints[i]] = unknownHeights[i];
        }

        var residuals = observations
            .Select(o => finalHeights[o.ToPoint] - finalHeights[o.FromPoint] - o.HeightDifference)
            .ToArray();

        double vpv = 0;
        for (int i = 0; i < observations.Count; i++)
        {
            vpv += AdjustmentMath.Weight(observations[i]) * residuals[i] * residuals[i];
        }

        // A connected open leveling run with exactly one known point can be
        // uniquely solvable with zero redundancy (degreesOfFreedom == 0).
        // Legacy AdjustLevel accepts this case: heights are propagated exactly,
        // residuals are zero, and no empirical unit-weight standard deviation can
        // be estimated. Preserve that behavior by reporting sigma0/error as zero.
        double sigma0 = degreesOfFreedom == 0
            ? 0
            : Math.Sqrt(vpv / degreesOfFreedom);

        var adjustedHeights = new List<AdjustedHeight>(points.Count);
        foreach (string point in points)
        {
            bool isKnown = knownMap.ContainsKey(point);
            double error = 0;
            if (!isKnown)
            {
                int index = unknownIndex[point];
                error = sigma0 * Math.Sqrt(Math.Max(0, qUnknown[index, index]));
            }

            adjustedHeights.Add(new AdjustedHeight(point, finalHeights[point], error, isKnown));
        }

        var adjustedDifferences = new List<AdjustedDifference>(observations.Count);
        for (int i = 0; i < observations.Count; i++)
        {
            var observation = observations[i];
            double varianceFactor = DifferenceCofactorClassical(
                observation.FromPoint,
                observation.ToPoint,
                unknownIndex,
                qUnknown);
            double standardError = sigma0 * Math.Sqrt(Math.Max(0, varianceFactor));
            double adjusted = finalHeights[observation.ToPoint] - finalHeights[observation.FromPoint];

            adjustedDifferences.Add(new AdjustedDifference(
                observation.Id,
                observation.FromPoint,
                observation.ToPoint,
                observation.HeightDifference,
                adjusted,
                residuals[i],
                observation.DistanceMeters,
                observation.StationCount,
                standardError));
        }

        return new AdjustmentResult(
            AdjustmentMethod.Classical,
            adjustedHeights,
            adjustedDifferences,
            sigma0,
            degreesOfFreedom,
            Iterations: 1,
            Converged: true);
    }

    private static void AddNormalEquation(
        double[,] normal,
        double[] rhs,
        IReadOnlyList<double> row,
        double target,
        double weight)
    {
        for (int i = 0; i < row.Count; i++)
        {
            if (row[i] == 0)
            {
                continue;
            }

            rhs[i] += weight * row[i] * target;
            for (int j = 0; j < row.Count; j++)
            {
                if (row[j] != 0)
                {
                    normal[i, j] += weight * row[i] * row[j];
                }
            }
        }
    }

    private static double DifferenceCofactorClassical(
        string from,
        string to,
        IReadOnlyDictionary<string, int> unknownIndex,
        double[,] qUnknown)
    {
        bool fromUnknown = unknownIndex.TryGetValue(from, out int i);
        bool toUnknown = unknownIndex.TryGetValue(to, out int j);

        return (fromUnknown, toUnknown) switch
        {
            (false, false) => 0,
            (true, false) => qUnknown[i, i],
            (false, true) => qUnknown[j, j],
            (true, true) => qUnknown[i, i] + qUnknown[j, j] - 2 * qUnknown[i, j]
        };
    }
}
