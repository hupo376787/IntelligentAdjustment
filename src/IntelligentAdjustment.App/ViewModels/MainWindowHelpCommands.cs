using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.Input;
using IntelligentAdjustment.App.Services;

namespace IntelligentAdjustment.App.ViewModels;

public partial class MainWindowViewModel
{
    [RelayCommand]
    private void OpenUserManual()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "Help",
            "Intelligent_Adjustment_User_Manual.pdf");

        if (!File.Exists(path))
        {
            dialogs.Error(
                LocalizationService.Text("Loc.Help.ManualMissing"),
                LocalizationService.Text("Loc.Menu.UserManual"));
            return;
        }

        try
        {
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
