using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using IntelligentAdjustment.App.Services;
using IntelligentAdjustment.App.ViewModels;
using IntelligentAdjustment.Application.Services;

namespace IntelligentAdjustment.App;

public partial class MainWindow : Window
{
    private bool closeApproved;
    private bool closeCheckInProgress;

    public MainWindow()
    {
        InitializeComponent();

        var dialogs = new UserDialogService();
        var session = new ProjectSessionService();
        var preferences = new ApplicationPreferencesService();
        ViewModel = new MainWindowViewModel(session, dialogs, preferences);
        ViewModel.RequestClose += (_, _) => Close();
        DataContext = ViewModel;
    }

    public MainWindowViewModel ViewModel { get; }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e) =>
        await ViewModel.InitializeAsync();

    private async void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (closeApproved)
        {
            return;
        }

        // Always cancel this closing pass first. CanCloseAsync can complete synchronously
        // (for example when the document is not dirty), and calling Close() again from
        // inside the active Closing event causes WPF to throw InvalidOperationException.
        e.Cancel = true;

        if (closeCheckInProgress)
        {
            return;
        }

        closeCheckInProgress = true;
        bool canClose;
        try
        {
            canClose = await ViewModel.CanCloseAsync();
        }
        finally
        {
            closeCheckInProgress = false;
        }

        if (!canClose)
        {
            return;
        }

        closeApproved = true;

        // Queue the real close after the current Closing event has returned.
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Normal,
            new Action(Close));
    }
}
