using System.Windows;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.App.Services;

public static class PointLabelLayout
{
    public static Point GetTopLeft(
        Point nodeCenter,
        double labelWidth,
        double labelHeight,
        PointNameHorizontalAlignmentMode horizontal,
        PointNameVerticalAlignmentMode vertical,
        double gap = 10)
    {
        double left = horizontal switch
        {
            PointNameHorizontalAlignmentMode.Left => nodeCenter.X - gap - labelWidth,
            PointNameHorizontalAlignmentMode.Center => nodeCenter.X - labelWidth / 2,
            PointNameHorizontalAlignmentMode.Right => nodeCenter.X + gap,
            _ => nodeCenter.X - labelWidth / 2
        };

        double top = vertical switch
        {
            PointNameVerticalAlignmentMode.Top => nodeCenter.Y - gap - labelHeight,
            PointNameVerticalAlignmentMode.Center => nodeCenter.Y - labelHeight / 2,
            PointNameVerticalAlignmentMode.Bottom => nodeCenter.Y + gap,
            _ => nodeCenter.Y - gap - labelHeight
        };

        return new Point(left, top);
    }
}
