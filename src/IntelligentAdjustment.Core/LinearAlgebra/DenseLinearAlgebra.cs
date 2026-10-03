namespace IntelligentAdjustment.Core.LinearAlgebra;

internal static class DenseLinearAlgebra
{
    private const double PivotTolerance = 1e-12;

    public static double[] Solve(double[,] matrix, double[] rightHandSide)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        ArgumentNullException.ThrowIfNull(rightHandSide);

        int n = matrix.GetLength(0);
        if (matrix.GetLength(1) != n || rightHandSide.Length != n)
        {
            throw new ArgumentException("Matrix dimensions do not match.");
        }

        var a = (double[,])matrix.Clone();
        var b = (double[])rightHandSide.Clone();

        for (int k = 0; k < n; k++)
        {
            int pivotRow = k;
            double pivotAbs = Math.Abs(a[k, k]);
            for (int row = k + 1; row < n; row++)
            {
                double candidate = Math.Abs(a[row, k]);
                if (candidate > pivotAbs)
                {
                    pivotAbs = candidate;
                    pivotRow = row;
                }
            }

            if (pivotAbs <= PivotTolerance)
            {
                throw new InvalidOperationException("Normal matrix is singular or ill-conditioned.");
            }

            if (pivotRow != k)
            {
                SwapRows(a, k, pivotRow);
                (b[k], b[pivotRow]) = (b[pivotRow], b[k]);
            }

            double pivot = a[k, k];
            for (int row = k + 1; row < n; row++)
            {
                double factor = a[row, k] / pivot;
                if (Math.Abs(factor) <= double.Epsilon)
                {
                    continue;
                }

                a[row, k] = 0;
                for (int col = k + 1; col < n; col++)
                {
                    a[row, col] -= factor * a[k, col];
                }

                b[row] -= factor * b[k];
            }
        }

        var x = new double[n];
        for (int row = n - 1; row >= 0; row--)
        {
            double sum = b[row];
            for (int col = row + 1; col < n; col++)
            {
                sum -= a[row, col] * x[col];
            }

            x[row] = sum / a[row, row];
        }

        return x;
    }

    public static double[,] Invert(double[,] matrix)
    {
        int n = matrix.GetLength(0);
        if (matrix.GetLength(1) != n)
        {
            throw new ArgumentException("Matrix must be square.", nameof(matrix));
        }

        var inverse = new double[n, n];
        for (int col = 0; col < n; col++)
        {
            var e = new double[n];
            e[col] = 1;
            var solution = Solve(matrix, e);
            for (int row = 0; row < n; row++)
            {
                inverse[row, col] = solution[row];
            }
        }

        return inverse;
    }

    public static double Norm2(IReadOnlyList<double> vector)
    {
        double sum = 0;
        for (int i = 0; i < vector.Count; i++)
        {
            sum += vector[i] * vector[i];
        }

        return Math.Sqrt(sum);
    }

    private static void SwapRows(double[,] matrix, int rowA, int rowB)
    {
        int cols = matrix.GetLength(1);
        for (int col = 0; col < cols; col++)
        {
            (matrix[rowA, col], matrix[rowB, col]) = (matrix[rowB, col], matrix[rowA, col]);
        }
    }
}
