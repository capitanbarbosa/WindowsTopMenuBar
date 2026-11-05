using System.Windows;
using System.Windows.Controls;

namespace WindowsTopMenuBar.Controls;

public sealed class StripPanel : Panel
{
    public static readonly DependencyProperty FlexProperty =
        DependencyProperty.RegisterAttached(
            "Flex",
            typeof(bool),
            typeof(StripPanel),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static readonly DependencyProperty IsVerticalProperty =
        DependencyProperty.Register(
            nameof(IsVertical),
            typeof(bool),
            typeof(StripPanel),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public bool IsVertical
    {
        get => (bool)GetValue(IsVerticalProperty);
        set => SetValue(IsVerticalProperty, value);
    }

    public static bool GetFlex(DependencyObject obj) => (bool)obj.GetValue(FlexProperty);
    public static void SetFlex(DependencyObject obj, bool value) => obj.SetValue(FlexProperty, value);

    protected override Size MeasureOverride(Size constraint)
    {
        bool vertical = IsVertical;
        double cross = vertical ? constraint.Width : constraint.Height;
        if (double.IsInfinity(cross) || cross < 1) cross = 36;

        double fixedMain = 0;
        int flex = 0;
        foreach (UIElement child in InternalChildren)
        {
            var available = vertical
                ? new Size(cross, double.PositiveInfinity)
                : new Size(double.PositiveInfinity, cross);
            child.Measure(available);
            if (GetFlex(child)) flex++;
            else fixedMain += vertical ? child.DesiredSize.Height : child.DesiredSize.Width;
        }

        double main = vertical ? constraint.Height : constraint.Width;
        if (double.IsInfinity(main) || main < 1) main = fixedMain + flex * 80;
        return vertical ? new Size(cross, main) : new Size(main, cross);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        bool vertical = IsVertical;
        double main = vertical ? finalSize.Height : finalSize.Width;
        double fixedMain = 0;
        int flexCount = 0;
        foreach (UIElement child in InternalChildren)
        {
            if (GetFlex(child)) flexCount++;
            else fixedMain += vertical ? child.DesiredSize.Height : child.DesiredSize.Width;
        }

        double extra = Math.Max(0, main - fixedMain);
        double flexEach = flexCount > 0 ? extra / flexCount : 0;
        double pos = 0;
        foreach (UIElement child in InternalChildren)
        {
            double length = GetFlex(child)
                ? flexEach
                : (vertical ? child.DesiredSize.Height : child.DesiredSize.Width);
            if (vertical)
                child.Arrange(new Rect(0, pos, finalSize.Width, length));
            else
                child.Arrange(new Rect(pos, 0, length, finalSize.Height));
            pos += length;
        }
        return finalSize;
    }
}
