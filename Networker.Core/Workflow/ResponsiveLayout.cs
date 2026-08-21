namespace Networker.Core.Workflow;

public enum ResponsiveWidthMode
{
    Compact,
    Standard,
    Wide,
}

public static class ResponsiveLayout
{
    // The stage strip needs roughly 1,000 logical units before its labels stop
    // competing with the shell. Keep the compact selector active above the
    // minimum window so 1920px displays at high DPI remain usable.
    public const double CompactMaxWidth = 1099.999;
    public const double StandardMaxWidth = 1399.999;

    public static ResponsiveWidthMode WidthMode(double logicalWidth)
        => logicalWidth <= CompactMaxWidth ? ResponsiveWidthMode.Compact
            : logicalWidth <= StandardMaxWidth ? ResponsiveWidthMode.Standard
            : ResponsiveWidthMode.Wide;

    public static bool IsCompact(double logicalWidth) => WidthMode(logicalWidth) == ResponsiveWidthMode.Compact;
}
