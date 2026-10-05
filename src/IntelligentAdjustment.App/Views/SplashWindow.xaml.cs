using System.Windows;

namespace IntelligentAdjustment.App.Views;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
    }

    public void UpdateProgress(StartupProgress progress)
    {
        double value = Math.Clamp(progress.Percentage, 0, 100);
        StartupProgressBar.Value = value;
        PercentText.Text = $"{value:0}%";
        WorkItemText.Text = progress.WorkItem;
        UpdateLayout();
    }
}
