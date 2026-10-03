using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IntelligentAdjustment.App.ViewModels;

namespace IntelligentAdjustment.App.Views;

public partial class LevelDifferencesView : UserControl
{
    public LevelDifferencesView()
    {
        InitializeComponent();
    }

    private void DifferenceGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not DataGrid grid || DataContext is not LevelDifferencesTabViewModel viewModel)
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

            return;
        }

        if (e.Key == Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (viewModel.DeleteSelectedCommand.CanExecute(null))
            {
                viewModel.DeleteSelectedCommand.Execute(null);
                e.Handled = true;
            }

            return;
        }

        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            MoveToNextRow(grid);
            e.Handled = true;
        }
    }

    private static void MoveToNextRow(DataGrid grid)
    {
        _ = grid.CommitEdit(DataGridEditingUnit.Cell, true);
        _ = grid.CommitEdit(DataGridEditingUnit.Row, true);

        if (grid.CurrentItem is null || grid.CurrentColumn is null)
        {
            return;
        }

        int currentIndex = grid.Items.IndexOf(grid.CurrentItem);
        int nextIndex = currentIndex + 1;
        if (nextIndex < 0 || nextIndex >= grid.Items.Count)
        {
            return;
        }

        object nextItem = grid.Items[nextIndex];
        grid.SelectedItem = nextItem;
        grid.CurrentCell = new DataGridCellInfo(nextItem, grid.CurrentColumn);
        grid.ScrollIntoView(nextItem, grid.CurrentColumn);
        grid.BeginEdit();
    }
}
