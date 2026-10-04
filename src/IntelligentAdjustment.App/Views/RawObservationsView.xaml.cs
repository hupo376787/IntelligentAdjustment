using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using IntelligentAdjustment.App.ViewModels;
using IntelligentAdjustment.Application.Observations;

namespace IntelligentAdjustment.App.Views;

public partial class RawObservationsView : UserControl
{
    private RawObservationsTabViewModel? viewModel;

    public RawObservationsView()
    {
        InitializeComponent();
        DataContextChanged += RawObservationsView_DataContextChanged;
        Unloaded += (_, _) => DetachViewModel();
    }

    private void RawObservationsView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DetachViewModel();
        viewModel = e.NewValue as RawObservationsTabViewModel;
        AttachViewModel();
        DrawProfile();
    }

    private void AttachViewModel()
    {
        if (viewModel is null)
        {
            return;
        }

        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        viewModel.Document.RawObservations.CollectionChanged += RawObservations_CollectionChanged;
        foreach (RawObservationRowViewModel row in viewModel.Document.RawObservations)
        {
            row.PropertyChanged += RawRow_PropertyChanged;
        }
    }

    private void DetachViewModel()
    {
        if (viewModel is null)
        {
            return;
        }

        viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        viewModel.Document.RawObservations.CollectionChanged -= RawObservations_CollectionChanged;
        foreach (RawObservationRowViewModel row in viewModel.Document.RawObservations)
        {
            row.PropertyChanged -= RawRow_PropertyChanged;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RawObservationsTabViewModel.SelectedLine))
        {
            DrawProfile();
        }
    }

    private void RawObservations_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (RawObservationRowViewModel row in e.OldItems)
            {
                row.PropertyChanged -= RawRow_PropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (RawObservationRowViewModel row in e.NewItems)
            {
                row.PropertyChanged += RawRow_PropertyChanged;
            }
        }

        DrawProfile();
    }

    private void RawRow_PropertyChanged(object? sender, PropertyChangedEventArgs e) => DrawProfile();

    private void ProfileExpander_Expanded(object sender, RoutedEventArgs e) => DrawProfile();

    private void ProfileCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawProfile();

    private void RawGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (DataContext is not RawObservationsTabViewModel vm ||
            e.Row.Item is not RawObservationRowViewModel row)
        {
            return;
        }

        string? header = e.Column.Header?.ToString();

        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(async () =>
            {
                await vm.HandleCellCommittedAsync(row, header);
                DrawProfile();
            }));
    }

    private void RawGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not DataGrid grid ||
            DataContext is not RawObservationsTabViewModel vm)
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

            if (vm.PasteRows(text))
            {
                e.Handled = true;
            }

            return;
        }

        if (e.Key == Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (vm.DeleteSelectedCommand.CanExecute(null))
            {
                vm.DeleteSelectedCommand.Execute(null);
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

    private void DrawProfile()
    {
        if (viewModel?.SelectedLine is null ||
            ProfileCanvas.ActualWidth < 50 ||
            ProfileCanvas.ActualHeight < 50)
        {
            return;
        }

        ProfileCanvas.Children.Clear();

        RawObservationRowViewModel[] rows = viewModel.Document.RawObservations
            .Where(x => x.LineId == viewModel.SelectedLine.Id)
            .OrderBy(x => x.Sequence)
            .ToArray();

        var points = new List<ProfilePoint>();
        double cumulativeDistance = 0;
        double cumulativeHeight = 0;

        string firstName = rows.FirstOrDefault()?.FromPoint ?? string.Empty;
        points.Add(new ProfilePoint(firstName, 0, 0));

        foreach (RawObservationRowViewModel row in rows)
        {
            if (!RawObservationDifferenceBuilder.TryBuild(
                    row.ToDomain(),
                    row.Id,
                    out var difference,
                    out _) ||
                difference is null)
            {
                continue;
            }

            cumulativeDistance += difference.DistanceMeters;
            cumulativeHeight += difference.HeightDifference;
            points.Add(new ProfilePoint(
                difference.ToPoint,
                cumulativeDistance,
                cumulativeHeight));
        }

        if (points.Count < 2)
        {
            AddProfileMessage("当前线路没有足够的有效原始观测用于绘制剖面。");
            return;
        }

        double left = 55;
        double top = 24;
        double right = 24;
        double bottom = 46;
        double width = Math.Max(1, ProfileCanvas.ActualWidth - left - right);
        double height = Math.Max(1, ProfileCanvas.ActualHeight - top - bottom);

        double minX = points.Min(x => x.Distance);
        double maxX = points.Max(x => x.Distance);
        double minY = points.Min(x => x.RelativeHeight);
        double maxY = points.Max(x => x.RelativeHeight);

        if (Math.Abs(maxX - minX) < 1e-9)
        {
            maxX = minX + 1;
        }

        if (Math.Abs(maxY - minY) < 1e-9)
        {
            minY -= 0.5;
            maxY += 0.5;
        }

        double yPadding = (maxY - minY) * 0.12;
        minY -= yPadding;
        maxY += yPadding;

        Point Map(ProfilePoint p) => new(
            left + (p.Distance - minX) / (maxX - minX) * width,
            top + (maxY - p.RelativeHeight) / (maxY - minY) * height);

        AddProfileLine(left, top + height, left + width, top + height, Brushes.Gray, 1);
        AddProfileLine(left, top, left, top + height, Brushes.Gray, 1);

        var polyline = new Polyline
        {
            Stroke = Brushes.SteelBlue,
            StrokeThickness = 2,
            IsHitTestVisible = false
        };

        foreach (ProfilePoint point in points)
        {
            Point screen = Map(point);
            polyline.Points.Add(screen);
        }

        ProfileCanvas.Children.Add(polyline);

        int labelStep = Math.Max(1, points.Count / 12);
        for (int i = 0; i < points.Count; i++)
        {
            ProfilePoint point = points[i];
            Point screen = Map(point);

            var dot = new Ellipse
            {
                Width = 7,
                Height = 7,
                Fill = Brushes.White,
                Stroke = Brushes.SteelBlue,
                StrokeThickness = 1.5,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(dot, screen.X - 3.5);
            Canvas.SetTop(dot, screen.Y - 3.5);
            ProfileCanvas.Children.Add(dot);

            if (i % labelStep == 0 || i == points.Count - 1)
            {
                var label = new TextBlock
                {
                    Text = point.PointName,
                    FontSize = 10,
                    Foreground = Brushes.DimGray,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(label, screen.X + 4);
                Canvas.SetTop(label, screen.Y - 17);
                ProfileCanvas.Children.Add(label);
            }
        }

        AddAxisText(
            $"累计距离 {maxX:F1} m",
            left + width / 2 - 45,
            top + height + 18);

        AddAxisText(
            $"相对高差范围 {(maxY - minY):F4} m",
            6,
            4);
    }

    private void AddProfileMessage(string message)
    {
        var text = new TextBlock
        {
            Text = message,
            Foreground = Brushes.Gray,
            FontSize = 13
        };
        Canvas.SetLeft(text, 18);
        Canvas.SetTop(text, 18);
        ProfileCanvas.Children.Add(text);
    }

    private void AddAxisText(string text, double x, double y)
    {
        var block = new TextBlock
        {
            Text = text,
            Foreground = Brushes.DimGray,
            FontSize = 10,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(block, x);
        Canvas.SetTop(block, y);
        ProfileCanvas.Children.Add(block);
    }

    private void AddProfileLine(
        double x1,
        double y1,
        double x2,
        double y2,
        Brush brush,
        double thickness)
    {
        ProfileCanvas.Children.Add(new Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = brush,
            StrokeThickness = thickness,
            IsHitTestVisible = false
        });
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

    private sealed record ProfilePoint(
        string PointName,
        double Distance,
        double RelativeHeight);
}
