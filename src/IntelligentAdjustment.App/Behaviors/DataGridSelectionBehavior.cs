using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace IntelligentAdjustment.App.Behaviors;

public static class DataGridSelectionBehavior
{
    public static readonly DependencyProperty SelectedItemsProperty =
        DependencyProperty.RegisterAttached(
            "SelectedItems",
            typeof(IList),
            typeof(DataGridSelectionBehavior),
            new PropertyMetadata(null, OnSelectedItemsChanged));

    public static void SetSelectedItems(DependencyObject element, IList value) =>
        element.SetValue(SelectedItemsProperty, value);

    public static IList? GetSelectedItems(DependencyObject element) =>
        (IList?)element.GetValue(SelectedItemsProperty);

    private static void OnSelectedItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid)
        {
            return;
        }

        grid.SelectionChanged -= Grid_SelectionChanged;
        if (e.NewValue is IList)
        {
            grid.SelectionChanged += Grid_SelectionChanged;
            Sync(grid);
        }
    }

    private static void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is DataGrid grid)
        {
            Sync(grid);
        }
    }

    private static void Sync(DataGrid grid)
    {
        IList? target = GetSelectedItems(grid);
        if (target is null || target.IsReadOnly || target.IsFixedSize)
        {
            return;
        }

        target.Clear();
        foreach (object item in grid.SelectedItems)
        {
            target.Add(item);
        }
    }
}
