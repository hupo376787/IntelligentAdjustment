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
        : this(new ApplicationPreferencesService())
    {
    }

    public MainWindow(ApplicationPreferencesService preferences)
    {
        InitializeComponent();

        var dialogs = new UserDialogService();
        var session = new ProjectSessionService();
        ViewModel = new MainWindowViewModel(session, dialogs, preferences);
        ViewModel.RequestClose += (_, _) => Close();
        DataContext = ViewModel;
    }

    public MainWindowViewModel ViewModel { get; }

    private async void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (closeApproved)
        {
            return;
        }

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
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Normal,
            new Action(Close));
    }
}
