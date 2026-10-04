using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IntelligentAdjustment.App.ViewModels;

namespace IntelligentAdjustment.App.Views;

public partial class KnownHeightsView : UserControl
{
    public KnownHeightsView()
    {
        InitializeComponent();
        PreviewKeyDown += KnownHeightsView_PreviewKeyDown;
    }

    private void KnownHeightsView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not KnownHeightsTabViewModel viewModel)
        {
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.V)
        {
            string text = Clipboard.ContainsText(TextDataFormat.UnicodeText)
                ? Clipboard.GetText(TextDataFormat.UnicodeText)
                : Clipboard.ContainsText()
                    ? Clipboard.GetText()
                    : string.Empty;

            if (viewModel.PasteRows(text))
            {
                e.Handled = true;
            }
        }
    }
}
