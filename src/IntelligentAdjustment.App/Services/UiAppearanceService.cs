using System.Windows;

namespace IntelligentAdjustment.App.Services;

public static class UiAppearanceService
{
    public const double DefaultFontSize = 13.0;

    public static double NormalizeFontSize(double value) =>
        Math.Round(Math.Clamp(value, 11.0, 18.0), 1);

    public static void ApplyFontSize(double value)
    {
        if (System.Windows.Application.Current is null)
        {
            return;
        }

        double size = NormalizeFontSize(value);
        System.Windows.Application.Current.Resources["AppFontSize"] = size;
        System.Windows.Application.Current.Resources["AppPageTitleFontSize"] = size + 11.0;
        System.Windows.Application.Current.Resources["AppSectionTitleFontSize"] = size + 7.0;
        System.Windows.Application.Current.Resources["AppSubsectionTitleFontSize"] = size + 3.0;
    }
}
