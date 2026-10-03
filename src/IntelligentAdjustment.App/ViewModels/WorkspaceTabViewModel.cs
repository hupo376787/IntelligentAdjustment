using CommunityToolkit.Mvvm.ComponentModel;

namespace IntelligentAdjustment.App.ViewModels;

public abstract class WorkspaceTabViewModel : ObservableObject
{
    protected WorkspaceTabViewModel(string key, string header)
    {
        Key = key;
        Header = header;
    }

    public string Key { get; }
    public string Header { get; }
}
