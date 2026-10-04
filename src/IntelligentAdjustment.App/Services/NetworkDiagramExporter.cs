using System.Globalization;
using System.Security;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IntelligentAdjustment.App.ViewModels;

namespace IntelligentAdjustment.App.Services;

public static class NetworkDiagramExporter
{
    public static void ExportPng(
        NetworkDiagramScene scene,
        string filePath,
        int width = 1800,
        int height = 1200)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        (double scale, Vector offset) = CalculateFit(scene, width, height, 90);
        var visual = new DrawingVisual();

        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            DrawScene(dc, scene, scale, offset, width, height);
        }

        var bitmap = new RenderTargetBitmap(
            width,
            height,
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using FileStream stream = File.Create(filePath);
        encoder.Save(stream);
    }

    public static void ExportSvg(
        NetworkDiagramScene scene,
        string filePath,
        int width = 1800,
        int height = 1200)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        (double scale, Vector offset) = CalculateFit(scene, width, height, 90);
        var positions = scene.Nodes.ToDictionary(
            x => x.PointName,
            x => Transform(x.Position, scale, offset),
            StringComparer.Ordinal);

        var builder = new StringBuilder();
        builder.AppendLine("""<?xml version="1.0" encoding="UTF-8"?>""");
        builder.AppendLine(
            $"<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}">");
        builder.AppendLine("""  <rect width="100%" height="100%" fill="white"/>""");

        foreach (DiagramSceneEdge edge in scene.Edges)
        {
            if (!positions.TryGetValue(edge.FromPoint, out Point from) ||
                !positions.TryGetValue(edge.ToPoint, out Point to))
            {
                continue;
            }

            string stroke = edge.IsOverLimit ? "#C62828" : "#7B8794";
            string dash = edge.IsOverLimit ? " stroke-dasharray="10 6"" : string.Empty;
            double thickness = edge.IsOverLimit ? 4 : 2;

            builder.AppendLine(
                FormattableString.Invariant(
                    $"  <line x1="{from.X:F3}" y1="{from.Y:F3}" x2="{to.X:F3}" y2="{to.Y:F3}" stroke="{stroke}" stroke-width="{thickness:F1}"{dash}/>"));
        }

        foreach (DiagramSceneNode node in scene.Nodes)
        {
            Point p = positions[node.PointName];
            string label = SecurityElement.Escape(node.PointName) ?? string.Empty;

            switch (node.Kind)
            {
                case DiagramNodeKind.Known:
                    builder.AppendLine(
                        FormattableString.Invariant(
                            $"  <rect x="{p.X - 8:F3}" y="{p.Y - 8:F3}" width="16" height="16" rx="2" fill="#1A237E" stroke="#1A237E" stroke-width="2"/>"));
                    break;

                case DiagramNodeKind.Transition:
                    builder.AppendLine(
                        FormattableString.Invariant(
                            $"  <polygon points="{p.X:F3},{p.Y - 9:F3} {p.X + 9:F3},{p.Y:F3} {p.X:F3},{p.Y + 9:F3} {p.X - 9:F3},{p.Y:F3}" fill="white" stroke="#EF6C00" stroke-width="3"/>"));
                    break;

                default:
                    builder.AppendLine(
                        FormattableString.Invariant(
                            $"  <circle cx="{p.X:F3}" cy="{p.Y:F3}" r="8" fill="white" stroke="#2F6F9F" stroke-width="3"/>"));
                    break;
            }

            if (!node.HasPersistedCoordinate)
            {
                builder.AppendLine(
                    FormattableString.Invariant(
                        $"  <circle cx="{p.X:F3}" cy="{p.Y:F3}" r="13" fill="none" stroke="#9CA3AF" stroke-width="1.5" stroke-dasharray="4 3"/>"));
            }

            builder.AppendLine(
                FormattableString.Invariant(
                    $"  <text x="{p.X + 12:F3}" y="{p.Y - 12:F3}" font-family="Segoe UI,Arial,sans-serif" font-size="16" fill="#111827">{label}</text>"));
        }

        builder.AppendLine("""  <g font-family="Segoe UI,Arial,sans-serif" font-size="15" fill="#374151">""");
        builder.AppendLine("""    <text x="28" y="34">■ 已知点　○ 平差点　◇ 过渡点　红色虚线：超限闭合/附合路线</text>""");
        if (scene.MissingCoordinateCount > 0)
        {
            builder.AppendLine(
                $"    <text x="28" y="58">注：{scene.MissingCoordinateCount} 个点未保存草图坐标，本图使用临时布局。</text>");
        }

        builder.AppendLine("  </g>");
        builder.AppendLine("</svg>");

        File.WriteAllText(filePath, builder.ToString(), new UTF8Encoding(false));
    }

    private static void DrawScene(
        DrawingContext dc,
        NetworkDiagramScene scene,
        double scale,
        Vector offset,
        double width,
        double height)
    {
        var positions = scene.Nodes.ToDictionary(
            x => x.PointName,
            x => Transform(x.Position, scale, offset),
            StringComparer.Ordinal);

        var normalPen = new Pen(new SolidColorBrush(Color.FromRgb(123, 135, 148)), 2);
        var overLimitPen = new Pen(new SolidColorBrush(Color.FromRgb(198, 40, 40)), 4)
        {
            DashStyle = new DashStyle(new double[] { 8, 5 }, 0)
        };

        foreach (DiagramSceneEdge edge in scene.Edges)
        {
            if (!positions.TryGetValue(edge.FromPoint, out Point from) ||
                !positions.TryGetValue(edge.ToPoint, out Point to))
            {
                continue;
            }

            dc.DrawLine(edge.IsOverLimit ? overLimitPen : normalPen, from, to);
        }

        var labelTypeface = new Typeface("Segoe UI");
        foreach (DiagramSceneNode node in scene.Nodes)
        {
            Point p = positions[node.PointName];

            switch (node.Kind)
            {
                case DiagramNodeKind.Known:
                    dc.DrawRectangle(
                        new SolidColorBrush(Color.FromRgb(26, 35, 126)),
                        new Pen(new SolidColorBrush(Color.FromRgb(26, 35, 126)), 2),
                        new Rect(p.X - 8, p.Y - 8, 16, 16));
                    break;

                case DiagramNodeKind.Transition:
                    var geometry = new StreamGeometry();
                    using (StreamGeometryContext gc = geometry.Open())
                    {
                        gc.BeginFigure(new Point(p.X, p.Y - 9), true, true);
                        gc.LineTo(new Point(p.X + 9, p.Y), true, false);
                        gc.LineTo(new Point(p.X, p.Y + 9), true, false);
                        gc.LineTo(new Point(p.X - 9, p.Y), true, false);
                    }

                    geometry.Freeze();
                    dc.DrawGeometry(
                        Brushes.White,
                        new Pen(new SolidColorBrush(Color.FromRgb(239, 108, 0)), 3),
                        geometry);
                    break;

                default:
                    dc.DrawEllipse(
                        Brushes.White,
                        new Pen(new SolidColorBrush(Color.FromRgb(47, 111, 159)), 3),
                        p,
                        8,
                        8);
                    break;
            }

            if (!node.HasPersistedCoordinate)
            {
                var temporaryPen = new Pen(Brushes.Gray, 1.5)
                {
                    DashStyle = new DashStyle(new double[] { 3, 3 }, 0)
                };
                dc.DrawEllipse(null, temporaryPen, p, 13, 13);
            }

            var text = new FormattedText(
                node.PointName,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                labelTypeface,
                16,
                Brushes.Black,
                1.0);

            dc.DrawText(text, new Point(p.X + 12, p.Y - 24));
        }

        var legend = new FormattedText(
            "■ 已知点   ○ 平差点   ◇ 过渡点   红色虚线：超限闭合/附合路线",
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            labelTypeface,
            15,
            Brushes.DimGray,
            1.0);
        dc.DrawText(legend, new Point(28, 22));

        if (scene.MissingCoordinateCount > 0)
        {
            var warning = new FormattedText(
                $"注：{scene.MissingCoordinateCount} 个点未保存草图坐标，本图使用临时布局。",
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                labelTypeface,
                14,
                Brushes.DarkOrange,
                1.0);
            dc.DrawText(warning, new Point(28, height - 42));
        }
    }

    private static (double Scale, Vector Offset) CalculateFit(
        NetworkDiagramScene scene,
        double width,
        double height,
        double margin)
    {
        if (scene.Nodes.Count == 0)
        {
            return (1, new Vector(width / 2, height / 2));
        }

        double minX = scene.Nodes.Min(x => x.Position.X);
        double maxX = scene.Nodes.Max(x => x.Position.X);
        double minY = scene.Nodes.Min(x => x.Position.Y);
        double maxY = scene.Nodes.Max(x => x.Position.Y);

        double sceneWidth = Math.Max(1, maxX - minX);
        double sceneHeight = Math.Max(1, maxY - minY);

        double scale = Math.Min(
            Math.Max(1, width - margin * 2) / sceneWidth,
            Math.Max(1, height - margin * 2) / sceneHeight);

        scale = Math.Clamp(scale, 0.02, 100);

        double centerX = (minX + maxX) / 2;
        double centerY = (minY + maxY) / 2;

        return (
            scale,
            new Vector(
                width / 2 - centerX * scale,
                height / 2 - centerY * scale));
    }

    private static Point Transform(Point world, double scale, Vector offset) =>
        new(world.X * scale + offset.X, world.Y * scale + offset.Y);
}
