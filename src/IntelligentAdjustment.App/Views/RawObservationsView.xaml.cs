using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using IntelligentAdjustment.App.ViewModels;

namespace IntelligentAdjustment.App.Views;

public partial class RawObservationsView : UserControl
{
    public RawObservationsView()
    {
        InitializeComponent();
    }

    private void RawGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (DataContext is not RawObservationsTabViewModel viewModel ||
            e.Row.Item is not RawObservationRowViewModel row)
        {
            return;
        }

        string? header = e.Column.Header?.ToString();

        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(async () =>
            {
                await viewModel.HandleCellCommittedAsync(row, header);
            }));
    }

    private void RawGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not DataGrid grid ||
            DataContext is not RawObservationsTabViewModel viewModel)
        {
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
