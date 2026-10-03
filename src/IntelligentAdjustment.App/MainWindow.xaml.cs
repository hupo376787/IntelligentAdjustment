using System.ComponentModel;
using System.Windows;
using IntelligentAdjustment.App.Services;
using IntelligentAdjustment.App.ViewModels;
using IntelligentAdjustment.Application.Services;

namespace IntelligentAdjustment.App;

public partial class MainWindow : Window
{
    private bool closeApproved;

    public MainWindow()
    {
        InitializeComponent();

        var dialogs = new UserDialogService();
        var session = new ProjectSessionService();
        ViewModel = new MainWindowViewModel(session, dialogs);
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
        if (await ViewModel.CanCloseAsync())
        {
            closeApproved = true;
            Close();
        }
    }
}
