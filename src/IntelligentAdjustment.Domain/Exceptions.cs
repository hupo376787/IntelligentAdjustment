namespace IntelligentAdjustment.Domain;

public class AdjustmentException : Exception
{
    public AdjustmentException(string message) : base(message)
    {
    }
}

public sealed class InvalidNetworkException : AdjustmentException
{
    public InvalidNetworkException(string message) : base(message)
    {
    }
}
