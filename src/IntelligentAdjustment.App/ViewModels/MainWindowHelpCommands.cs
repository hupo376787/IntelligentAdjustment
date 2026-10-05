using System.Diagnostics;
using CommunityToolkit.Mvvm.Input;
using IntelligentAdjustment.App.Services;

namespace IntelligentAdjustment.App.ViewModels;

public partial class MainWindowViewModel
{
    [RelayCommand]
    private void OpenUserManual()
    {
        try
        {
            string path = BundledUserManual.GetOrCreatePath();
            Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            dialogs.Error(
                LocalizationService.Format("Loc.Help.ManualOpenFailed", ex.Message),
                LocalizationService.Text("Loc.Menu.UserManual"));
        }
    }
}
