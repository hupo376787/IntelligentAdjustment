using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using IntelligentAdjustment.App.Services;
using IntelligentAdjustment.App.Views;

namespace IntelligentAdjustment.App;

public partial class App : System.Windows.Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var preferences = new ApplicationPreferencesService();
        LocalizationService.ApplyLanguage(preferences.LanguageCode);
        UiAppearanceService.ApplyFontSize(preferences.UiFontSize);

        var splashDuration = Stopwatch.StartNew();
        var splash = new SplashWindow();
        splash.Show();
        splash.UpdateProgress(new StartupProgress(8, LocalizationService.Text("Loc.Splash.LoadingResources")));
        await Dispatcher.Yield(DispatcherPriority.Loaded);

        splash.UpdateProgress(new StartupProgress(18, LocalizationService.Text("Loc.Splash.ReadingSettings")));
        var mainWindow = new MainWindow(preferences);
        MainWindow = mainWindow;
        await Dispatcher.Yield(DispatcherPriority.Background);

        splash.UpdateProgress(new StartupProgress(28, LocalizationService.Text("Loc.Splash.InitializingWorkspace")));
        await mainWindow.ViewModel.InitializeAsync(splash.UpdateProgress);

        splash.UpdateProgress(new StartupProgress(94, LocalizationService.Text("Loc.Splash.FinishingLayout")));
        await Dispatcher.Yield(DispatcherPriority.Loaded);

        // Keep the splash visible for at least three seconds. Slow startups are
        // not delayed further; fast startups only wait for the remaining time.
        TimeSpan minimumSplashDuration = TimeSpan.FromSeconds(3);
        TimeSpan remaining = minimumSplashDuration - splashDuration.Elapsed;
        if (remaining > TimeSpan.Zero)
        {
            splash.UpdateProgress(new StartupProgress(98, LocalizationService.Text("Loc.Splash.PreparingMainWindow")));
            await Task.Delay(remaining);
        }

        splash.UpdateProgress(new StartupProgress(100, LocalizationService.Text("Loc.Splash.Done")));
        mainWindow.Show();
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        splash.Close();
    }
}
