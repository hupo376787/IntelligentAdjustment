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
        UiAppearanceService.ApplyFontSize(preferences.UiFontSize);

        var splashDuration = Stopwatch.StartNew();
        var splash = new SplashWindow();
        splash.Show();
        splash.UpdateProgress(new StartupProgress(8, "正在加载界面资源…"));
        await Dispatcher.Yield(DispatcherPriority.Loaded);

        splash.UpdateProgress(new StartupProgress(18, "正在读取应用设置…"));
        var mainWindow = new MainWindow(preferences);
        MainWindow = mainWindow;
        await Dispatcher.Yield(DispatcherPriority.Background);

        splash.UpdateProgress(new StartupProgress(28, "正在初始化工作区…"));
        await mainWindow.ViewModel.InitializeAsync(splash.UpdateProgress);

        splash.UpdateProgress(new StartupProgress(94, "正在完成界面布局…"));
        await Dispatcher.Yield(DispatcherPriority.Loaded);

        // Keep the splash visible for at least three seconds. Slow startups are
        // not delayed further; fast startups only wait for the remaining time.
        TimeSpan minimumSplashDuration = TimeSpan.FromSeconds(3);
        TimeSpan remaining = minimumSplashDuration - splashDuration.Elapsed;
        if (remaining > TimeSpan.Zero)
        {
            splash.UpdateProgress(new StartupProgress(98, "正在准备主界面…"));
            await Task.Delay(remaining);
        }

        splash.UpdateProgress(new StartupProgress(100, "启动完成"));
        mainWindow.Show();
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        splash.Close();
    }
}
