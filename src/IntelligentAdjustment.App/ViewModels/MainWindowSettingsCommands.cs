using CommunityToolkit.Mvvm.Input;

namespace IntelligentAdjustment.App.ViewModels;

public partial class MainWindowViewModel
{
    [RelayCommand]
    private void OpenProjectSettings()
    {
        SelectedTab = GetOrCreateTab(
            "settings",
            () => new SettingsTabViewModel(
                Document,
                preferences,
                dialogs,
                () => CurrentProjectPath));
    }
}
