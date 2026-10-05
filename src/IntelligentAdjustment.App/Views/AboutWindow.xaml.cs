using System.Reflection;
using System.Windows;

namespace IntelligentAdjustment.App.Views;

public partial class AboutWindow
{
    public AboutWindow()
    {
        InitializeComponent();

        Assembly assembly = typeof(AboutWindow).Assembly;
        string version = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            ?? assembly.GetName().Version?.ToString(3)
            ?? "1.0.0";

        int metadataSeparator = version.IndexOf('+', StringComparison.Ordinal);
        if (metadataSeparator >= 0)
        {
            version = version[..metadataSeparator];
        }

        VersionText = $"Version {version}";
        CopyrightText = assembly
            .GetCustomAttribute<AssemblyCopyrightAttribute>()?
            .Copyright
            ?? $"© {DateTime.Now.Year} IntelligentAdjustment";

        DataContext = this;
    }

    public string VersionText { get; }

    public string CopyrightText { get; }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
