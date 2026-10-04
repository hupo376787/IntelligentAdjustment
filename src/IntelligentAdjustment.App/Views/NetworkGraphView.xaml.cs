using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using IntelligentAdjustment.App.Services;
using IntelligentAdjustment.App.ViewModels;
using Microsoft.Win32;

namespace IntelligentAdjustment.App.Views;

public partial class NetworkGraphView : UserControl
{
    private NetworkGraphTabViewModel? viewModel;
    private NetworkDiagramScene? scene;
    private double scale = 1.0;
    private Vector offset = new(60, 60);
    private bool isPanning;
    private Point lastPanScreen;

    public NetworkGraphView()
    {
        InitializeComponent();
        DataContextChanged += NetworkGraphView_DataContextChanged;
        Unloaded += (_, _) => DetachViewModel();
    }

    private void NetworkGraphView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DetachViewModel();
        viewModel = e.NewValue as NetworkGraphTabViewModel;
        AttachViewModel();
        RebuildScene();
        FitToView();
    }

    private void AttachViewModel()
    {
        if (viewModel is null)
        {
            return;
        }

        viewModel.FitRequested += ViewModel_FitRequested;
        viewModel.RedrawRequested += ViewModel_RedrawRequested;
        viewModel.ExportRequested += ViewModel_ExportRequested;
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    private void DetachViewModel()
    {
        if (viewModel is null)
        {
            return;
        }

        viewModel.FitRequested -= ViewModel_FitRequested;
        viewModel.RedrawRequested -= ViewModel_RedrawRequested;
        viewModel.ExportRequested -= ViewModel_ExportRequested;
        viewModel.PropertyChanged -= ViewModel_PropertyChanged;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NetworkGraphTabViewModel.SelectedDifference))
        {
            Redraw();
        }
    }

    private void ViewModel_FitRequested(object? sender, EventArgs e) => FitToView();

    private void ViewModel_RedrawRequested(object? sender, EventArgs e)
    {
        RebuildScene();
        Redraw();
    }

    private void ViewModel_ExportRequested(object? sender, DiagramExportFormat format)
    {
        if (viewModel is null)
        {
            return;
        }

        NetworkDiagramScene exportScene = viewModel.BuildScene();
        string extension = format == DiagramExportFormat.Png ? ".png" : ".svg";
        var dialog = new SaveFileDialog
        {
            Title = format == DiagramExportFormat.Png ? "导出水准网图 PNG" : "导出水准网图 SVG",
            Filter = format == DiagramExportFormat.Png
                ? "PNG 图像 (*.png)|*.png"
                : "SVG 矢量图 (*.svg)|*.svg",
            DefaultExt = extension,
            AddExtension = true,
            FileName = $"水准网图{extension}"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (format == DiagramExportFormat.Png)
        {
            NetworkDiagramExporter.ExportPng(exportScene, dialog.FileName);
        }
        else
        {
            NetworkDiagramExporter.ExportSvg(exportScene, dialog.FileName);
        }

        MessageBox.Show(
            $"已导出：{dialog.FileName}",
            "IntelligentAdjustment",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void GraphCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.PreviousSize.Width <= 0 || e.PreviousSize.Height <= 0)
        {
            FitToView();
        }
        else
        {
            Redraw();
        }
    }

    private void GraphCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        Point cursor = e.GetPosition(GraphCanvas);
        Point world = ScreenToWorld(cursor);
        double factor = e.Delta > 0 ? 1.12 : 1.0 / 1.12;

        scale = Math.Clamp(scale * factor, 0.05, 30.0);
        offset = new Vector(
            cursor.X - world.X * scale,
            cursor.Y - world.Y * scale);

        Redraw();
        e.Handled = true;
    }

    private void GraphCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle ||
            (e.ChangedButton == MouseButton.Left && Keyboard.IsKeyDown(Key.Space)))
        {
            isPanning = true;
            lastPanScreen = e.GetPosition(GraphCanvas);
            GraphCanvas.CaptureMouse();
            e.Handled = true;
        }
    }

    private void GraphCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!isPanning)
        {
            return;
        }

        Point current = e.GetPosition(GraphCanvas);
        offset += current - lastPanScreen;
        lastPanScreen = current;
        Redraw();
    }

    private void GraphCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!isPanning)
        {
            return;
        }

        isPanning = false;
        GraphCanvas.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void GraphCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (isPanning || viewModel is null || scene is null)
        {
            return;
        }

        Point screen = e.GetPosition(GraphCanvas);
        DiagramSceneEdge? edge = HitTestEdge(screen);
        viewModel.SelectedDifference = edge?.Difference;
        e.Handled = edge is not null;
    }

    private DiagramSceneEdge? HitTestEdge(Point screen)
    {
        if (scene is null)
        {
            return null;
        }

        var positions = scene.Nodes.ToDictionary(
            x => x.PointName,
            x => WorldToScreen(x.Position),
            StringComparer.Ordinal);

        DiagramSceneEdge? best = null;
        double bestDistance = 9.0;

        foreach (DiagramSceneEdge edge in scene.Edges)
        {
            if (!positions.TryGetValue(edge.FromPoint, out Point from) ||
                !positions.TryGetValue(edge.ToPoint, out Point to))
            {
                continue;
            }

            double distance = DistanceToSegment(screen, from, to);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = edge;
            }
        }

        return best;
    }

    private void RebuildScene()
    {
        scene = viewModel?.BuildScene();
    }

    private void FitToView()
    {
        if (scene is null ||
            GraphCanvas.ActualWidth <= 1 ||
            GraphCanvas.ActualHeight <= 1)
        {
            return;
        }

        if (scene.Nodes.Count == 0)
        {
            scale = 1;
            offset = new Vector(60, 60);
            Redraw();
            return;
        }

        double minX = scene.Nodes.Min(x => x.Position.X);
        double maxX = scene.Nodes.Max(x => x.Position.X);
        double minY = scene.Nodes.Min(x => x.Position.Y);
        double maxY = scene.Nodes.Max(x => x.Position.Y);

        double width = Math.Max(1, maxX - minX);
        double height = Math.Max(1, maxY - minY);
        double availableWidth = Math.Max(100, GraphCanvas.ActualWidth - 140);
        double availableHeight = Math.Max(100, GraphCanvas.ActualHeight - 160);

        scale = Math.Clamp(
            Math.Min(availableWidth / width, availableHeight / height),
            0.05,
            30.0);

        double centerX = (minX + maxX) / 2;
        double centerY = (minY + maxY) / 2;

        offset = new Vector(
            GraphCanvas.ActualWidth / 2 - centerX * scale,
            GraphCanvas.ActualHeight / 2 - centerY * scale);

        Redraw();
    }

    private void Redraw()
    {
        if (scene is null ||
            GraphCanvas.ActualWidth <= 0 ||
            GraphCanvas.ActualHeight <= 0)
        {
            return;
        }

        GraphCanvas.Children.Clear();

        var positions = scene.Nodes.ToDictionary(
            x => x.PointName,
            x => WorldToScreen(x.Position),
            StringComparer.Ordinal);

        foreach (DiagramSceneEdge edge in scene.Edges)
        {
            if (!positions.TryGetValue(edge.FromPoint, out Point from) ||
                !positions.TryGetValue(edge.ToPoint, out Point to))
            {
                continue;
            }

            bool selected = viewModel?.SelectedDifference is not null &&
                            ReferenceEquals(edge.Difference, viewModel.SelectedDifference);

            var line = new Line
            {
                X1 = from.X,
                Y1 = from.Y,
                X2 = to.X,
                Y2 = to.Y,
                Stroke = selected
                    ? Brushes.DodgerBlue
                    : edge.IsOverLimit
                        ? Brushes.Firebrick
                        : Brushes.SlateGray,
                StrokeThickness = selected ? 5 : edge.IsOverLimit ? 4 : 2,
                IsHitTestVisible = false
            };

            if (edge.IsOverLimit && !selected)
            {
                line.StrokeDashArray = new DoubleCollection { 7, 4 };
            }

            GraphCanvas.Children.Add(line);
        }

        foreach (DiagramSceneNode node in scene.Nodes)
        {
            Point p = positions[node.PointName];
            Shape shape = CreateNodeShape(node);
            Canvas.SetLeft(shape, p.X - 8);
            Canvas.SetTop(shape, p.Y - 8);
            GraphCanvas.Children.Add(shape);

            var text = new TextBlock
            {
                Text = node.PointName,
                FontSize = 12,
                Foreground = Brushes.Black,
                Background = new SolidColorBrush(Color.FromArgb(215, 255, 255, 255)),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(text, p.X + 11);
            Canvas.SetTop(text, p.Y - 19);
            GraphCanvas.Children.Add(text);

            if (!node.HasPersistedCoordinate)
            {
                var ring = new Ellipse
                {
                    Width = 26,
                    Height = 26,
                    Stroke = Brushes.Gray,
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 3, 3 },
                    Fill = Brushes.Transparent,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(ring, p.X - 13);
                Canvas.SetTop(ring, p.Y - 13);
                GraphCanvas.Children.Add(ring);
            }
        }

        DrawLegend();
    }

    private static Shape CreateNodeShape(DiagramSceneNode node)
    {
        Shape shape = node.Kind switch
        {
            DiagramNodeKind.Known => new Rectangle
            {
                Width = 16,
                Height = 16,
                RadiusX = 2,
                RadiusY = 2,
                Fill = Brushes.MidnightBlue,
                Stroke = Brushes.MidnightBlue,
                StrokeThickness = 2
            },
            DiagramNodeKind.Transition => new Polygon
            {
                Points = new PointCollection
                {
                    new(8, 0),
                    new(16, 8),
                    new(8, 16),
                    new(0, 8)
                },
                Fill = Brushes.White,
                Stroke = Brushes.DarkOrange,
                StrokeThickness = 2
            },
            _ => new Ellipse
            {
                Width = 16,
                Height = 16,
                Fill = Brushes.White,
                Stroke = Brushes.SteelBlue,
                StrokeThickness = 2
            }
        };

        shape.IsHitTestVisible = false;
        return shape;
    }

    private void DrawLegend()
    {
        var legend = new TextBlock
        {
            Text = "■ 已知点   ○ 平差点   ◇ 过渡点   红色虚线：超限路线   灰色虚环：临时坐标",
            Foreground = Brushes.DimGray,
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)),
            IsHitTestVisible = false
        };

        Canvas.SetLeft(legend, 18);
        Canvas.SetTop(legend, 14);
        GraphCanvas.Children.Add(legend);
    }

    private Point WorldToScreen(Point world) =>
        new(world.X * scale + offset.X, world.Y * scale + offset.Y);

    private Point ScreenToWorld(Point screen) =>
        new((screen.X - offset.X) / scale, (screen.Y - offset.Y) / scale);

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        Vector ab = b - a;
        double lengthSquared = ab.X * ab.X + ab.Y * ab.Y;
        if (lengthSquared <= 1e-12)
        {
            return (p - a).Length;
        }

        Vector ap = p - a;
        double t = Math.Clamp(
            (ap.X * ab.X + ap.Y * ab.Y) / lengthSquared,
            0,
            1);

        Point projection = a + ab * t;
        return (p - projection).Length;
    }
}
