namespace MobileMapper.Core;

public readonly record struct VideoGeometry(int Width, int Height, long Generation)
{
    public void Validate()
    {
        if (Width is < 1 or > 8192 || Height is < 1 or > 8192 || Generation < 1)
            throw new ArgumentOutOfRangeException(nameof(Width), "Unsupported video geometry.");
    }
}

public readonly record struct NormalizedPoint(double X, double Y)
{
    public (int X, int Y) ToPixels(VideoGeometry geometry)
    {
        geometry.Validate();
        if (!double.IsFinite(X) || !double.IsFinite(Y) || X is < 0 or > 1 || Y is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(X));
        return (Math.Min(geometry.Width - 1, (int)(X * geometry.Width)),
            Math.Min(geometry.Height - 1, (int)(Y * geometry.Height)));
    }
}

public static class Viewport
{
    // All panel quantities are DIPs. Equal DPI factors cancel; render uses the same aspect fit.
    public static NormalizedPoint? Map(double x, double y, double panelWidth, double panelHeight, VideoGeometry geometry)
    {
        geometry.Validate();
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(panelWidth) ||
            !double.IsFinite(panelHeight) || panelWidth <= 0 || panelHeight <= 0) return null;
        double fit = Math.Min(panelWidth / geometry.Width, panelHeight / geometry.Height);
        double width = geometry.Width * fit, height = geometry.Height * fit;
        double left = (panelWidth - width) / 2, top = (panelHeight - height) / 2;
        if (x < left || y < top || x >= left + width || y >= top + height) return null;
        return new((x - left) / width, (y - top) / height);
    }
}
