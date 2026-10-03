namespace IntelligentAdjustment.App.ViewModels;

public sealed class PlaceholderTabViewModel : WorkspaceTabViewModel
{
    public PlaceholderTabViewModel(string key, string header, string message)
        : base(key, header)
    {
        Message = message;
    }

    public string Message { get; }
}
