using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace PixPolish;

/// <summary>A collage layout: a name and the cells it is made of, as fractions (0..1) of the canvas.</summary>
public record CollageLayout(string Name, RectangleF[] Cells)
{
    public int Count => Cells.Length;
}

public static class CollageLayouts
{
    static RectangleF R(float x, float y, float w, float h) => new(x, y, w, h);
    const float T = 1f / 3f;

    public static readonly IReadOnlyList<CollageLayout> All = new CollageLayout[]
    {
        new("Single",              new[] { R(0, 0, 1, 1) }),
        new("Two rows",            new[] { R(0, 0, 1, .5f), R(0, .5f, 1, .5f) }),
        new("Two columns",         new[] { R(0, 0, .5f, 1), R(.5f, 0, .5f, 1) }),
        new("Narrow + wide",       new[] { R(0, 0, T, 1), R(T, 0, 1 - T, 1) }),
        new("Two on top, one below", new[] { R(0, 0, .5f, .5f), R(.5f, 0, .5f, .5f), R(0, .5f, 1, .5f) }),
        new("One on top, two below", new[] { R(0, 0, 1, .5f), R(0, .5f, .5f, .5f), R(.5f, .5f, .5f, .5f) }),
        new("One left, two right", new[] { R(0, 0, .5f, 1), R(.5f, 0, .5f, .5f), R(.5f, .5f, .5f, .5f) }),
        new("Two left, one right", new[] { R(0, 0, .5f, .5f), R(0, .5f, .5f, .5f), R(.5f, 0, .5f, 1) }),
        new("Three columns",       new[] { R(0, 0, T, 1), R(T, 0, T, 1), R(2 * T, 0, T, 1) }),
        new("Three rows",          new[] { R(0, 0, 1, T), R(0, T, 1, T), R(0, 2 * T, 1, T) }),
        new("Grid 2x2",            new[] { R(0, 0, .5f, .5f), R(.5f, 0, .5f, .5f), R(0, .5f, .5f, .5f), R(.5f, .5f, .5f, .5f) }),
        new("Mosaic 4",            new[] { R(0, 0, .6f, .6f), R(.6f, 0, .4f, .6f), R(0, .6f, .4f, .4f), R(.4f, .6f, .6f, .4f) }),
        new("Big top, three below", new[] { R(0, 0, 1, .6f), R(0, .6f, T, .4f), R(T, .6f, T, .4f), R(2 * T, .6f, T, .4f) }),
        new("Big left, three right", new[] { R(0, 0, .6f, 1), R(.6f, 0, .4f, T), R(.6f, T, .4f, T), R(.6f, 2 * T, .4f, T) }),
        new("Big left, four right", new[] { R(0, 0, .5f, 1), R(.5f, 0, .25f, .5f), R(.75f, 0, .25f, .5f), R(.5f, .5f, .25f, .5f), R(.75f, .5f, .25f, .5f) }),
        new("Two on top, three below", new[] { R(0, 0, .5f, .5f), R(.5f, 0, .5f, .5f), R(0, .5f, T, .5f), R(T, .5f, T, .5f), R(2 * T, .5f, T, .5f) }),
        new("Grid 3x2",            Grid(3, 2)),
        new("Grid 3x3",            Grid(3, 3)),
    };

    static RectangleF[] Grid(int cols, int rows) =>
        Enumerable.Range(0, cols * rows)
            .Select(i => R(i % cols / (float)cols, i / cols / (float)rows, 1f / cols, 1f / rows)).ToArray();

    /// <summary>Finds a layout by 1-based number or (part of) its name.</summary>
    public static CollageLayout? Find(string key) =>
        int.TryParse(key, out var n) ? (n >= 1 && n <= All.Count ? All[n - 1] : null)
        : All.FirstOrDefault(l => l.Name.Contains(key, StringComparison.OrdinalIgnoreCase));
}

public record CollageOptions(
    CollageLayout Layout,
    int Width = 1600,
    int Height = 1200,
    double SpacingPercent = 5,   // gap between photos (UI slider 0-25); 25 = 5% of the shorter canvas side
    double RadiusPercent = 0,    // photo corner radius, % of the shorter cell side
    string Background = "#FFFFFF");

/// <summary>Renders collages. Loaded photos are cached; <c>maxSide</c> downsizes them (use it for fast previews).</summary>
public sealed class CollageRenderer : IDisposable
{
    readonly int _maxSide;
    readonly Dictionary<string, Bitmap?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public CollageRenderer(int maxSide = 0) => _maxSide = maxSide;

    public Bitmap Render(CollageOptions o, IReadOnlyList<string> photos)
    {
        var canvas = new Bitmap(o.Width, o.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(canvas);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(ParseColor(o.Background));

        float gap = (float)(Math.Min(o.Width, o.Height) * o.SpacingPercent / 100.0 * 0.2);
        for (int i = 0; i < o.Layout.Count; i++)
        {
            var c = o.Layout.Cells[i];
            // Gap between cells, and the same gap as outer margin.
            float left = c.Left * o.Width, top = c.Top * o.Height, right = c.Right * o.Width, bottom = c.Bottom * o.Height;
            left += c.Left <= 0.0001f ? gap : gap / 2;
            top += c.Top <= 0.0001f ? gap : gap / 2;
            right -= c.Right >= 0.9999f ? gap : gap / 2;
            bottom -= c.Bottom >= 0.9999f ? gap : gap / 2;
            var cell = Rectangle.Round(RectangleF.FromLTRB(left, top, right, bottom));
            if (cell.Width < 2 || cell.Height < 2) continue;

            float radius = (float)(Math.Min(cell.Width, cell.Height) * o.RadiusPercent / 100.0);
            using var path = RoundedRect(cell, radius);
            var photo = i < photos.Count ? Load(photos[i]) : null;
            if (photo == null)
            {
                using var empty = new SolidBrush(Color.FromArgb(235, 237, 240));
                g.FillPath(empty, path);
                continue;
            }
            // Fill the cell ("cover"): scale up and centre-crop, then paint it as a texture so rounded edges stay anti-aliased.
            using var fitted = Cover(photo, cell.Size);
            using var brush = new TextureBrush(fitted);
            brush.TranslateTransform(cell.X, cell.Y);
            g.FillPath(brush, path);
        }
        return canvas;
    }

    public byte[] RenderPng(CollageOptions o, IReadOnlyList<string> photos)
    {
        using var bmp = Render(o, photos);
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    Bitmap? Load(string path)
    {
        if (_cache.TryGetValue(path, out var cached)) return cached;
        Bitmap? result = null;
        try
        {
            using var src = new Bitmap(path);
            double scale = _maxSide > 0 ? Math.Min(1.0, (double)_maxSide / Math.Max(src.Width, src.Height)) : 1.0;
            result = new Bitmap(Math.Max(1, (int)(src.Width * scale)), Math.Max(1, (int)(src.Height * scale)), PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(result);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(src, 0, 0, result.Width, result.Height);
        }
        catch { /* unreadable file: leave the cell empty */ }
        _cache[path] = result;
        return result;
    }

    static Bitmap Cover(Bitmap src, Size size)
    {
        double scale = Math.Max((double)size.Width / src.Width, (double)size.Height / src.Height);
        float srcW = (float)(size.Width / scale), srcH = (float)(size.Height / scale);
        var srcRect = new RectangleF((src.Width - srcW) / 2, (src.Height - srcH) / 2, srcW, srcH);
        var result = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(src, new Rectangle(Point.Empty, size), srcRect.X, srcRect.Y, srcRect.Width, srcRect.Height, GraphicsUnit.Pixel);
        return result;
    }

    static GraphicsPath RoundedRect(Rectangle r, float radius)
    {
        var path = new GraphicsPath();
        if (radius < 1) { path.AddRectangle(r); return path; }
        float d = radius * 2;
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static Color ParseColor(string value)
    {
        if (string.Equals(value, "transparent", StringComparison.OrdinalIgnoreCase)) return Color.Transparent;
        try { return ColorTranslator.FromHtml(value); } catch { return Color.White; }
    }

    public void Dispose()
    {
        foreach (var b in _cache.Values) b?.Dispose();
        _cache.Clear();
    }
}
