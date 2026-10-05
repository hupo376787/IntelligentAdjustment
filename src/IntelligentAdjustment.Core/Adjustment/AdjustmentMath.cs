using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.Core.Adjustment;

internal static class AdjustmentMath
{
    public static IReadOnlyList<string> CollectNetworkPoints(IReadOnlyList<LevelDifference> observations) =>
        observations
            .SelectMany(x => new[] { x.FromPoint, x.ToPoint })
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    public static void ValidateObservations(IReadOnlyList<LevelDifference> observations)
    {
        if (observations.Count == 0)
        {
            throw new InvalidNetworkException("The network has no level-difference observations.");
        }

        foreach (var observation in observations)
        {
            if (string.IsNullOrWhiteSpace(observation.FromPoint) || string.IsNullOrWhiteSpace(observation.ToPoint))
            {
                throw new InvalidNetworkException("Point names cannot be empty.");
            }

            if (observation.FromPoint == observation.ToPoint)
            {
                throw new InvalidNetworkException($"Observation {observation.Id} has the same from/to point.");
            }

            if (observation.DistanceMeters <= 0)
            {
                throw new InvalidNetworkException($"Observation {observation.Id} has a non-positive distance.");
            }

            if (observation.StationCount <= 0)
            {
                throw new InvalidNetworkException($"Observation {observation.Id} has an invalid station count.");
            }
        }
    }

    public static double Weight(LevelDifference observation) => 1.0 / observation.DistanceKilometers;

    public static IReadOnlyDictionary<string, List<string>> BuildAdjacency(IReadOnlyList<LevelDifference> observations)
    {
        var adjacency = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var observation in observations)
        {
            Add(adjacency, observation.FromPoint, observation.ToPoint);
            Add(adjacency, observation.ToPoint, observation.FromPoint);
        }

        return adjacency;
    }

    public static void EnsureConnected(IReadOnlyList<LevelDifference> observations)
    {
        var points = CollectNetworkPoints(observations);
        var adjacency = BuildAdjacency(observations);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(points[0]);
        visited.Add(points[0]);

        while (queue.Count > 0)
        {
            string point = queue.Dequeue();
            foreach (string next in adjacency[point])
            {
                if (visited.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }

        if (visited.Count != points.Count)
        {
            throw new InvalidNetworkException("The current leveling network is disconnected.");
        }
    }

    private static void Add(Dictionary<string, List<string>> adjacency, string from, string to)
    {
        if (!adjacency.TryGetValue(from, out var list))
        {
            list = new List<string>();
            adjacency[from] = list;
        }

        list.Add(to);
    }
}
