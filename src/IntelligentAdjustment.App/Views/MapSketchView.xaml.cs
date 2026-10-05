using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using IntelligentAdjustment.App.Services;
using IntelligentAdjustment.App.ViewModels;
using Microsoft.Win32;
using HandyMessageBox = HandyControl.Controls.MessageBox;

namespace IntelligentAdjustment.App.Views;

public partial class MapSketchView : UserControl
{
    private readonly HashSet<string> selectedPoints = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Point> dragStartWorld = new(StringComparer.Ordinal);

    private MapSketchTabViewModel? viewModel;
    private double scale = 1.0;
    private Vector offset = new(60, 60);
    private bool isPanning;
    private bool isDraggingNodes;
    private bool isMarquee;
    private Point mouseStartScreen;
    private Point lastScreen;
    private Point marqueeCurrent;
    private Vector previewWorldDelta;

    public MapSketchView()
    {
        InitializeComponent();
        DataContextChanged += MapSketchView_DataContextChanged;
        Unloaded += (_, _) => DetachViewModel();
    }

    private void MapSketchView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DetachViewModel();
        viewModel = e.NewValue as MapSketchTabViewModel;
        AttachViewModel();
        Redraw();
    }

    private void AttachViewModel()
    {
        if (viewModel is null)
        {
            return;
        }

        viewModel.FitRequested += ViewModel_FitRequested;
        viewModel.RedrawRequested += ViewModel_RedrawRequested;
        viewModel.Document.MapPoints.CollectionChanged += MapPoints_CollectionChanged;
        viewModel.Document.LevelDifferences.CollectionChanged += LevelDifferences_CollectionChanged;
        viewModel.Document.PropertyChanged += Document_PropertyChanged;

        foreach (NetworkMapPointRowViewModel point in viewModel.Document.MapPoints)
        {
            point.PropertyChanged += MapPoint_PropertyChanged;
        }
    }

    private void DetachViewModel()
    {
        if (viewModel is null)
        {
            return;
        }

        viewModel.FitRequested -= ViewModel_FitRequested;
        viewModel.RedrawRequested -= ViewModel_RedrawRequested;
        viewModel.Document.MapPoints.CollectionChanged -= MapPoints_CollectionChanged;
        viewModel.Document.LevelDifferences.CollectionChanged -= LevelDifferences_CollectionChanged;
        viewModel.Document.PropertyChanged -= Document_PropertyChanged;

        foreach (NetworkMapPointRowViewModel point in viewModel.Document.MapPoints)
        {
            point.PropertyChanged -= MapPoint_PropertyChanged;
        }
    }

    private void MapPoints_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (NetworkMapPointRowViewModel point in e.OldItems)
            {
                point.PropertyChanged -= MapPoint_PropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (NetworkMapPointRowViewModel point in e.NewItems)
            {
                point.PropertyChanged += MapPoint_PropertyChanged;
            }
        }

        Redraw();
    }

    private void LevelDifferences_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Redraw();

    private void Document_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProjectDocumentViewModel.Settings))
        {
            Redraw();
        }
    }

    private void MapPoint_PropertyChanged(object? sender, PropertyChangedEventArgs e) => Redraw();

    private void ViewModel_RedrawRequested(object? sender, EventArgs e) => Redraw();

    private void ViewModel_FitRequested(object? sender, EventArgs e) => FitToView();

    private void SketchCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
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

    private void SketchCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        Point cursor = e.GetPosition(SketchCanvas);
        Point world = ScreenToWorld(cursor);

        double factor = e.Delta > 0 ? 1.12 : 1.0 / 1.12;
        scale = Math.Clamp(scale * factor, 0.05, 30.0);
        offset = new Vector(
            cursor.X - world.X * scale,
            cursor.Y - world.Y * scale);

        Redraw();
        e.Handled = true;
    }

    private void SketchCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle ||
            (e.ChangedButton == MouseButton.Left && Keyboard.IsKeyDown(Key.Space)))
        {
            isPanning = true;
            lastScreen = e.GetPosition(SketchCanvas);
            SketchCanvas.CaptureMouse();
            e.Handled = true;
        }
    }

    private void SketchCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (isPanning && (e.ChangedButton == MouseButton.Middle || e.ChangedButton == MouseButton.Left))
        {
            isPanning = false;
            SketchCanvas.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void SketchCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (isPanning || viewModel is null)
        {
            return;
        }

        SketchCanvas.Focus();
        Point screen = e.GetPosition(SketchCanvas);
        string? hit = HitTestNode(screen);

        if (viewModel.IsPlacementMode && !string.IsNullOrWhiteSpace(viewModel.SelectedPointName))
        {
            Point world = ScreenToWorld(screen);
            viewModel.PlacePoint(viewModel.SelectedPointName, world.X, world.Y);
            selectedPoints.Clear();
            selectedPoints.Add(viewModel.SelectedPointName);
            Redraw();
            e.Handled = true;
            return;
        }

        if (hit is not null)
        {
            if (e.ClickCount >= 2)
            {
                viewModel.BeginRelocate(hit);
                e.Handled = true;
                return;
            }

            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                if (!selectedPoints.Add(hit))
                {
                    selectedPoints.Remove(hit);
                }
            }
            else if (!selectedPoints.Contains(hit))
            {
                selectedPoints.Clear();
                selectedPoints.Add(hit);
            }

            if (selectedPoints.Contains(hit))
            {
                dragStartWorld.Clear();
                foreach (NetworkMapPointRowViewModel point in viewModel.Document.MapPoints.Where(x => selectedPoints.Contains(x.PointName)))
                {
                    dragStartWorld[point.PointName] = new Point(point.X, point.Y);
                }

                if (dragStartWorld.Count > 0)
                {
                    isDraggingNodes = true;
                    mouseStartScreen = screen;
                    previewWorldDelta = new Vector();
                    SketchCanvas.CaptureMouse();
                }
            }

            Redraw();
            e.Handled = true;
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            selectedPoints.Clear();
        }

        isMarquee = true;
        mouseStartScreen = screen;
        marqueeCurrent = screen;
        SketchCanvas.CaptureMouse();
        Redraw();
        e.Handled = true;
    }

    private void SketchCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        Point current = e.GetPosition(SketchCanvas);

        if (isPanning)
        {
            Vector delta = current - lastScreen;
            offset += delta;
            lastScreen = current;
            Redraw();
            return;
        }

        if (isDraggingNodes)
        {
            Vector deltaScreen = current - mouseStartScreen;
            previewWorldDelta = new Vector(deltaScreen.X / scale, deltaScreen.Y / scale);
            Redraw();
            return;
        }

        if (isMarquee)
        {
            marqueeCurrent = current;
            Redraw();
        }
    }

    private void SketchCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (viewModel is null)
        {
            return;
        }

        if (isDraggingNodes)
        {
            isDraggingNodes = false;
            SketchCanvas.ReleaseMouseCapture();

            Vector delta = previewWorldDelta;
            previewWorldDelta = new Vector();
            if (Math.Abs(delta.X) > 1e-9 || Math.Abs(delta.Y) > 1e-9)
            {
                viewModel.MovePoints(selectedPoints.ToArray(), delta.X, delta.Y);
            }

            Redraw();
            e.Handled = true;
            return;
        }

        if (isMarquee)
        {
            isMarquee = false;
            SketchCanvas.ReleaseMouseCapture();
            marqueeCurrent = e.GetPosition(SketchCanvas);

            Rect rect = NormalizeRect(mouseStartScreen, marqueeCurrent);
            foreach (NetworkMapPointRowViewModel point in viewModel.Document.MapPoints)
            {
                Point screen = WorldToScreen(new Point(point.X, point.Y));
                if (rect.Contains(screen))
                {
                    selectedPoints.Add(point.PointName);
                }
            }

            Redraw();
            e.Handled = true;
        }
    }

    private void SketchCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (viewModel is null)
        {
            return;
        }

        Point screen = e.GetPosition(SketchCanvas);
        string? hit = HitTestNode(screen);
        if (hit is null)
        {
            ShowCanvasContextMenu();
            e.Handled = true;
            return;
        }

        if (!selectedPoints.Contains(hit))
        {
            selectedPoints.Clear();
            selectedPoints.Add(hit);
            Redraw();
        }

        var menu = new ContextMenu();
        var relocate = new MenuItem { Header = $"重新定位“{hit}”" };
        relocate.Click += (_, _) => viewModel.BeginRelocate(hit);

        var delete = new MenuItem { Header = "删除所选点的草图坐标" };
        delete.Click += (_, _) =>
        {
            viewModel.DeleteCoordinates(selectedPoints.ToArray());
            selectedPoints.Clear();
            Redraw();
        };

        menu.Items.Add(relocate);
        menu.Items.Add(new Separator());
        menu.Items.Add(delete);
        OpenContextMenu(menu);
        e.Handled = true;
    }

    private void ShowCanvasContextMenu()
    {
        if (viewModel is null)
        {
            return;
        }

        var menu = new ContextMenu();

        var autoMissing = new MenuItem
        {
            Header = "自动布局未定位点",
            IsEnabled = viewModel.UnpositionedPointNames.Count > 0
        };
        autoMissing.Click += (_, _) => viewModel.AutoLayoutUnpositionedCommand.Execute(null);

        var autoAll = new MenuItem
        {
            Header = "重新自动布局全部点",
            IsEnabled = viewModel.NetworkPointNames.Count > 0
        };
        autoAll.Click += (_, _) => viewModel.AutoLayoutAllPoints();

        var fit = new MenuItem
        {
            Header = "Fit to View",
            IsEnabled = viewModel.Document.MapPoints.Count > 0
        };
        fit.Click += (_, _) => FitToView();

        var clear = new MenuItem
        {
            Header = "清空全部点位坐标",
            IsEnabled = viewModel.Document.MapPoints.Count > 0
        };
        clear.Click += (_, _) =>
        {
            MessageBoxResult result = HandyMessageBox.Show(
                Window.GetWindow(this),
                "确定清空全部草图点位坐标吗？\n\n只删除草图坐标，不会删除网络点和观测数据；此操作可以使用 Ctrl+Z 撤销。",
                "清空网形草图",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            selectedPoints.Clear();
            viewModel.ClearAllCoordinates();
            Redraw();
        };

        var exportPng = new MenuItem
        {
            Header = "导出草图图片（PNG）",
            IsEnabled = viewModel.Document.MapPoints.Count > 0
        };
        exportPng.Click += (_, _) => ExportSketchPng();

        var exportSvg = new MenuItem
        {
            Header = "导出草图矢量图（SVG）",
            IsEnabled = viewModel.Document.MapPoints.Count > 0
        };
        exportSvg.Click += (_, _) => ExportSketchSvg();

        menu.Items.Add(autoMissing);
        menu.Items.Add(autoAll);
        menu.Items.Add(new Separator());
        menu.Items.Add(fit);
        menu.Items.Add(new Separator());
        menu.Items.Add(clear);
        menu.Items.Add(new Separator());
        menu.Items.Add(exportPng);
        menu.Items.Add(exportSvg);

        OpenContextMenu(menu);
    }

    private void OpenContextMenu(ContextMenu menu)
    {
        menu.PlacementTarget = SketchCanvas;
        menu.IsOpen = true;
    }

    private void ExportSketchPng()
    {
        if (viewModel is null ||
            SketchCanvas.ActualWidth <= 1 ||
            SketchCanvas.ActualHeight <= 1)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "导出网形草图图片",
            Filter = "PNG 图像 (*.png)|*.png",
            DefaultExt = ".png",
            AddExtension = true,
            FileName = "网形草图.png"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        double originalScale = scale;
        Vector originalOffset = offset;
        try
        {
            FitToView();
            SketchCanvas.UpdateLayout();

            int width = Math.Max(1, (int)Math.Ceiling(SketchCanvas.ActualWidth));
            int height = Math.Max(1, (int)Math.Ceiling(SketchCanvas.ActualHeight));
            var bitmap = new RenderTargetBitmap(
                width,
                height,
                96,
                96,
                PixelFormats.Pbgra32);
            bitmap.Render(SketchCanvas);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            using FileStream stream = File.Create(dialog.FileName);
            encoder.Save(stream);
            viewModel.StatusText = $"已导出网形草图：{dialog.FileName}";
        }
        finally
        {
            scale = originalScale;
            offset = originalOffset;
            Redraw();
        }
    }

    private void ExportSketchSvg()
    {
        if (viewModel is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "导出网形草图矢量图",
            Filter = "SVG 矢量图 (*.svg)|*.svg",
            DefaultExt = ".svg",
            AddExtension = true,
            FileName = "网形草图.svg"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        NetworkDiagramScene scene = NetworkDiagramSceneBuilder.Build(
            viewModel.Document,
            Array.Empty<IntelligentAdjustment.Domain.NetworkRoute>(),
            viewModel.Document.Settings);

        NetworkDiagramExporter.ExportSvg(
            scene,
            dialog.FileName,
            settings: viewModel.Document.Settings);

        viewModel.StatusText = $"已导出网形草图矢量图：{dialog.FileName}";
    }

    private string? HitTestNode(Point screen)
    {
        if (viewModel is null)
        {
            return null;
        }

        string? best = null;
        double bestDistance = 14.0;

        foreach (NetworkMapPointRowViewModel point in viewModel.Document.MapPoints)
        {
            Point world = new(point.X, point.Y);
            if (isDraggingNodes && dragStartWorld.TryGetValue(point.PointName, out Point start))
            {
                world = new Point(start.X + previewWorldDelta.X, start.Y + previewWorldDelta.Y);
            }

            Point candidate = WorldToScreen(world);
            double distance = (candidate - screen).Length;
            if (distance <= bestDistance)
            {
                bestDistance = distance;
                best = point.PointName;
            }
        }

        return best;
    }

    private void FitToView()
    {
        if (viewModel is null ||
            SketchCanvas.ActualWidth <= 1 ||
            SketchCanvas.ActualHeight <= 1)
        {
            return;
        }

        NetworkMapPointRowViewModel[] points = viewModel.Document.MapPoints.ToArray();
        if (points.Length == 0)
        {
            scale = 1.0;
            offset = new Vector(60, 60);
            Redraw();
            return;
        }

        double minX = points.Min(x => x.X);
        double maxX = points.Max(x => x.X);
        double minY = points.Min(x => x.Y);
        double maxY = points.Max(x => x.Y);

        double width = Math.Max(1, maxX - minX);
        double height = Math.Max(1, maxY - minY);
        double availableWidth = Math.Max(100, SketchCanvas.ActualWidth - 120);
        double availableHeight = Math.Max(100, SketchCanvas.ActualHeight - 140);

        scale = Math.Clamp(
            Math.Min(availableWidth / width, availableHeight / height),
            0.05,
            30.0);

        double centerWorldX = (minX + maxX) / 2;
        double centerWorldY = (minY + maxY) / 2;
        offset = new Vector(
            SketchCanvas.ActualWidth / 2 - centerWorldX * scale,
            SketchCanvas.ActualHeight / 2 - centerWorldY * scale);

        Redraw();
    }

    private void Redraw()
    {
        if (viewModel is null ||
            SketchCanvas.ActualWidth <= 0 ||
            SketchCanvas.ActualHeight <= 0)
        {
            return;
        }

        SketchCanvas.Children.Clear();
        DrawGrid();
        DrawEdges();
        DrawNodes();
        DrawScaleBar();

        if (isMarquee)
        {
            Rect rect = NormalizeRect(mouseStartScreen, marqueeCurrent);
            var selection = new Rectangle
            {
                Width = rect.Width,
                Height = rect.Height,
                Stroke = Brushes.DodgerBlue,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                Fill = new SolidColorBrush(Color.FromArgb(24, 30, 144, 255)),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(selection, rect.Left);
            Canvas.SetTop(selection, rect.Top);
            SketchCanvas.Children.Add(selection);
        }
    }

    private void DrawGrid()
    {
        if (viewModel is null || !viewModel.SnapEnabled || viewModel.SnapSize <= 0)
        {
            return;
        }

        double pixelSpacing = viewModel.SnapSize * scale;
        if (pixelSpacing < 12)
        {
            return;
        }

        double leftWorld = ScreenToWorld(new Point(0, 0)).X;
        double rightWorld = ScreenToWorld(new Point(SketchCanvas.ActualWidth, 0)).X;
        double topWorld = ScreenToWorld(new Point(0, 0)).Y;
        double bottomWorld = ScreenToWorld(new Point(0, SketchCanvas.ActualHeight)).Y;
        double step = viewModel.SnapSize;

        double startX = Math.Floor(leftWorld / step) * step;
        for (double x = startX; x <= rightWorld; x += step)
        {
            double sx = WorldToScreen(new Point(x, 0)).X;
            AddLine(sx, 0, sx, SketchCanvas.ActualHeight, Brushes.WhiteSmoke, 1);
        }

        double startY = Math.Floor(topWorld / step) * step;
        for (double y = startY; y <= bottomWorld; y += step)
        {
            double sy = WorldToScreen(new Point(0, y)).Y;
            AddLine(0, sy, SketchCanvas.ActualWidth, sy, Brushes.WhiteSmoke, 1);
        }
    }

    private void DrawEdges()
    {
        if (viewModel is null)
        {
            return;
        }

        var positions = viewModel.Document.MapPoints.ToDictionary(
            x => x.PointName,
            x => new Point(x.X, x.Y),
            StringComparer.Ordinal);

        foreach (LevelDifferenceRowViewModel edge in viewModel.Document.LevelDifferences)
        {
            if (!positions.TryGetValue(edge.FromPoint, out Point from) ||
                !positions.TryGetValue(edge.ToPoint, out Point to))
            {
                continue;
            }

            AddLine(
                WorldToScreen(from),
                WorldToScreen(to),
                new SolidColorBrush(Color.FromRgb(190, 197, 205)),
                1.2);
        }
    }

    private void DrawNodes()
    {
        if (viewModel is null)
        {
            return;
        }

        foreach (NetworkMapPointRowViewModel point in viewModel.Document.MapPoints)
        {
            Point world = new(point.X, point.Y);
            if (isDraggingNodes && dragStartWorld.TryGetValue(point.PointName, out Point start))
            {
                world = new Point(start.X + previewWorldDelta.X, start.Y + previewWorldDelta.Y);
            }

            Point screen = WorldToScreen(world);
            bool selected = selectedPoints.Contains(point.PointName);
            DiagramNodeKind kind = viewModel.GetNodeKind(point.PointName);

            Shape shape = kind switch
            {
                DiagramNodeKind.Known => new Rectangle
                {
                    Width = 14,
                    Height = 14,
                    RadiusX = 2,
                    RadiusY = 2,
                    Fill = Brushes.MidnightBlue
                },
                DiagramNodeKind.Transition => new Polygon
                {
                    Points = new PointCollection
                    {
                        new(7, 0),
                        new(14, 7),
                        new(7, 14),
                        new(0, 7)
                    },
                    Fill = Brushes.White,
                    Stroke = Brushes.DarkOrange,
                    StrokeThickness = 2
                },
                _ => new Ellipse
                {
                    Width = 14,
                    Height = 14,
                    Fill = Brushes.White,
                    Stroke = Brushes.SteelBlue,
                    StrokeThickness = 2
                }
            };

            if (selected)
            {
                shape.Stroke = Brushes.DodgerBlue;
                shape.StrokeThickness = 3;
            }

            shape.IsHitTestVisible = false;
            Canvas.SetLeft(shape, screen.X - 7);
            Canvas.SetTop(shape, screen.Y - 7);
            SketchCanvas.Children.Add(shape);

            var label = new TextBlock
            {
                Text = point.PointName,
                Foreground = Brushes.Black,
                FontSize = 12,
                Background = new SolidColorBrush(Color.FromArgb(210, 255, 255, 255)),
                IsHitTestVisible = false
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Point labelPosition = PointLabelLayout.GetTopLeft(
                screen,
                label.DesiredSize.Width,
                label.DesiredSize.Height,
                viewModel.Document.Settings.PointNameHorizontalAlignment,
                viewModel.Document.Settings.PointNameVerticalAlignment,
                10);
            Canvas.SetLeft(label, labelPosition.X);
            Canvas.SetTop(label, labelPosition.Y);
            SketchCanvas.Children.Add(label);
        }
    }

    private void DrawScaleBar()
    {
        if (SketchCanvas.ActualWidth < 180 || SketchCanvas.ActualHeight < 100)
        {
            return;
        }

        const double targetPixels = 120;
        double rawWorld = targetPixels / scale;
        double worldLength = NiceNumber(rawWorld);
        double pixels = worldLength * scale;

        double x = 28;
        double y = SketchCanvas.ActualHeight - 34;
        AddLine(x, y, x + pixels, y, Brushes.Black, 2);
        AddLine(x, y - 5, x, y + 5, Brushes.Black, 2);
        AddLine(x + pixels, y - 5, x + pixels, y + 5, Brushes.Black, 2);

        var label = new TextBlock
        {
            Text = $"{worldLength:0.###} 图形单位",
            FontSize = 11,
            Foreground = Brushes.DimGray,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(label, x);
        Canvas.SetTop(label, y - 24);
        SketchCanvas.Children.Add(label);

        ScaleText.Text = $"缩放 {scale:0.###}×";
    }

    private static double NiceNumber(double value)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            return 1;
        }

        double exponent = Math.Pow(10, Math.Floor(Math.Log10(value)));
        double fraction = value / exponent;
        double nice = fraction < 1.5 ? 1 : fraction < 3.5 ? 2 : fraction < 7.5 ? 5 : 10;
        return nice * exponent;
    }

    private void AddLine(Point from, Point to, Brush brush, double thickness) =>
        AddLine(from.X, from.Y, to.X, to.Y, brush, thickness);

    private void AddLine(double x1, double y1, double x2, double y2, Brush brush, double thickness)
    {
        SketchCanvas.Children.Add(new Line
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

    private Point WorldToScreen(Point world) =>
        new(world.X * scale + offset.X, world.Y * scale + offset.Y);

    private Point ScreenToWorld(Point screen) =>
        new((screen.X - offset.X) / scale, (screen.Y - offset.Y) / scale);

    private static Rect NormalizeRect(Point a, Point b) =>
        new(
            Math.Min(a.X, b.X),
            Math.Min(a.Y, b.Y),
            Math.Abs(a.X - b.X),
            Math.Abs(a.Y - b.Y));
}
