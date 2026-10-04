using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;
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
            DrawScene(dc, scene, scale, offset, height);
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

        var settings = new XmlWriterSettings
        {
            Indent = true,
            Encoding = new System.Text.UTF8Encoding(false)
        };

        using XmlWriter writer = XmlWriter.Create(filePath, settings);
        writer.WriteStartDocument();
        writer.WriteStartElement("svg", "http://www.w3.org/2000/svg");
        writer.WriteAttributeString("width", width.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("height", height.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString(
            "viewBox",
            FormattableString.Invariant($"0 0 {width} {height}"));

        writer.WriteStartElement("rect");
        writer.WriteAttributeString("width", "100%");
        writer.WriteAttributeString("height", "100%");
        writer.WriteAttributeString("fill", "white");
        writer.WriteEndElement();

        foreach (DiagramSceneEdge edge in scene.Edges)
        {
            if (!positions.TryGetValue(edge.FromPoint, out Point from) ||
                !positions.TryGetValue(edge.ToPoint, out Point to))
            {
                continue;
            }

            writer.WriteStartElement("line");
            WriteDoubleAttribute(writer, "x1", from.X);
            WriteDoubleAttribute(writer, "y1", from.Y);
            WriteDoubleAttribute(writer, "x2", to.X);
            WriteDoubleAttribute(writer, "y2", to.Y);
            writer.WriteAttributeString("stroke", edge.IsOverLimit ? "#C62828" : "#7B8794");
            writer.WriteAttributeString("stroke-width", edge.IsOverLimit ? "4" : "2");

            if (edge.IsOverLimit)
            {
                writer.WriteAttributeString("stroke-dasharray", "10 6");
            }

            writer.WriteEndElement();
        }

        foreach (DiagramSceneNode node in scene.Nodes)
        {
            Point p = positions[node.PointName];

            switch (node.Kind)
            {
                case DiagramNodeKind.Known:
                    writer.WriteStartElement("rect");
                    WriteDoubleAttribute(writer, "x", p.X - 8);
                    WriteDoubleAttribute(writer, "y", p.Y - 8);
                    writer.WriteAttributeString("width", "16");
                    writer.WriteAttributeString("height", "16");
                    writer.WriteAttributeString("rx", "2");
                    writer.WriteAttributeString("fill", "#1A237E");
                    writer.WriteAttributeString("stroke", "#1A237E");
                    writer.WriteAttributeString("stroke-width", "2");
                    writer.WriteEndElement();
                    break;

                case DiagramNodeKind.Transition:
                    writer.WriteStartElement("polygon");
                    writer.WriteAttributeString(
                        "points",
                        FormattableString.Invariant(
                            $"{p.X:F3},{p.Y - 9:F3} " +
                            $"{p.X + 9:F3},{p.Y:F3} " +
                            $"{p.X:F3},{p.Y + 9:F3} " +
                            $"{p.X - 9:F3},{p.Y:F3}"));
                    writer.WriteAttributeString("fill", "white");
                    writer.WriteAttributeString("stroke", "#EF6C00");
                    writer.WriteAttributeString("stroke-width", "3");
                    writer.WriteEndElement();
                    break;

                default:
                    writer.WriteStartElement("circle");
                    WriteDoubleAttribute(writer, "cx", p.X);
                    WriteDoubleAttribute(writer, "cy", p.Y);
                    writer.WriteAttributeString("r", "8");
                    writer.WriteAttributeString("fill", "white");
                    writer.WriteAttributeString("stroke", "#2F6F9F");
                    writer.WriteAttributeString("stroke-width", "3");
                    writer.WriteEndElement();
                    break;
            }

            if (!node.HasPersistedCoordinate)
            {
                writer.WriteStartElement("circle");
                WriteDoubleAttribute(writer, "cx", p.X);
                WriteDoubleAttribute(writer, "cy", p.Y);
                writer.WriteAttributeString("r", "13");
                writer.WriteAttributeString("fill", "none");
                writer.WriteAttributeString("stroke", "#9CA3AF");
                writer.WriteAttributeString("stroke-width", "1.5");
                writer.WriteAttributeString("stroke-dasharray", "4 3");
                writer.WriteEndElement();
            }

            writer.WriteStartElement("text");
            WriteDoubleAttribute(writer, "x", p.X + 12);
            WriteDoubleAttribute(writer, "y", p.Y - 12);
            writer.WriteAttributeString("font-family", "Segoe UI,Arial,sans-serif");
            writer.WriteAttributeString("font-size", "16");
            writer.WriteAttributeString("fill", "#111827");
            writer.WriteString(node.PointName);
            writer.WriteEndElement();
        }

        writer.WriteStartElement("g");
        writer.WriteAttributeString("font-family", "Segoe UI,Arial,sans-serif");
        writer.WriteAttributeString("font-size", "15");
        writer.WriteAttributeString("fill", "#374151");

        writer.WriteStartElement("text");
        writer.WriteAttributeString("x", "28");
        writer.WriteAttributeString("y", "34");
        writer.WriteString("■ 已知点　○ 平差点　◇ 过渡点　红色虚线：超限闭合/附合路线");
        writer.WriteEndElement();

        if (scene.MissingCoordinateCount > 0)
        {
            writer.WriteStartElement("text");
            writer.WriteAttributeString("x", "28");
            writer.WriteAttributeString("y", "58");
            writer.WriteString($"注：{scene.MissingCoordinateCount} 个点未保存草图坐标，本图使用临时布局。");
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private static void DrawScene(
        DrawingContext dc,
        NetworkDiagramScene scene,
        double scale,
        Vector offset,
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

        double fitScale = Math.Min(
            Math.Max(1, width - margin * 2) / sceneWidth,
            Math.Max(1, height - margin * 2) / sceneHeight);

        fitScale = Math.Clamp(fitScale, 0.02, 100);

        double centerX = (minX + maxX) / 2;
        double centerY = (minY + maxY) / 2;

        return (
            fitScale,
            new Vector(
                width / 2 - centerX * fitScale,
                height / 2 - centerY * fitScale));
    }

    private static Point Transform(Point world, double scale, Vector offset) =>
        new(world.X * scale + offset.X, world.Y * scale + offset.Y);

    private static void WriteDoubleAttribute(XmlWriter writer, string name, double value) =>
        writer.WriteAttributeString(name, value.ToString("F3", CultureInfo.InvariantCulture));
}
