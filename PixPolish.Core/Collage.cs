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

/// <summary>
/// A draggable line between cells: a vertical line (x = Pos) or horizontal line (y = Pos), spanning From..To on the other axis.
/// Moving it resizes the cells that end at it (Before) and start at it (After).
/// </summary>
public record Divider(bool Vertical, float Pos, float From, float To, int[] Before, int[] After);

public static class CollageDividers
{
    const float Eps = 0.002f;
    public const float MinCellSize = 0.1f; // fraction of the canvas

    /// <summary>The lines between cells that can be dragged. Collinear cells share one line, so a grid column/row moves together.</summary>
    public static List<Divider> Find(IReadOnlyList<RectangleF> cells)
    {
        var result = new List<Divider>();
        foreach (bool vertical in new[] { true, false })
        {
            float End(RectangleF c) => vertical ? c.Right : c.Bottom;
            float Start(RectangleF c) => vertical ? c.Left : c.Top;
            var positions = new List<float>();
            foreach (var c in cells)
                if (End(c) < 1 - Eps && !positions.Any(p => Math.Abs(p - End(c)) < Eps)) positions.Add(End(c));

            foreach (float pos in positions.Order())
            {
                var before = Enumerable.Range(0, cells.Count).Where(i => Math.Abs(End(cells[i]) - pos) < Eps).ToArray();
                var after = Enumerable.Range(0, cells.Count).Where(i => Math.Abs(Start(cells[i]) - pos) < Eps).ToArray();
                if (before.Length == 0 || after.Length == 0) continue;
                var involved = before.Concat(after).Select(i => cells[i]).ToList();
                float from = involved.Min(c => vertical ? c.Top : c.Left), to = involved.Max(c => vertical ? c.Bottom : c.Right);
                result.Add(new Divider(vertical, pos, from, to, before, after));
            }
        }
        return result;
    }

    /// <summary>Moves a line to <paramref name="newPos"/> (clamped so no cell gets smaller than <see cref="MinCellSize"/>).</summary>
    public static RectangleF[] Move(IReadOnlyList<RectangleF> cells, Divider d, float newPos)
    {
        var result = cells.ToArray();
        float lo = d.Before.Max(i => d.Vertical ? cells[i].Left : cells[i].Top) + MinCellSize;
        float hi = d.After.Min(i => d.Vertical ? cells[i].Right : cells[i].Bottom) - MinCellSize;
        if (lo > hi) return result;
        newPos = Math.Clamp(newPos, lo, hi);

        foreach (int i in d.Before)
        {
            var c = cells[i];
            result[i] = d.Vertical ? RectangleF.FromLTRB(c.Left, c.Top, newPos, c.Bottom) : RectangleF.FromLTRB(c.Left, c.Top, c.Right, newPos);
        }
        foreach (int i in d.After)
        {
            var c = cells[i];
            result[i] = d.Vertical ? RectangleF.FromLTRB(newPos, c.Top, c.Right, c.Bottom) : RectangleF.FromLTRB(c.Left, newPos, c.Right, c.Bottom);
        }
        return result;
    }
}

public record CollageOptions(
    CollageLayout Layout,
    int Width = 1600,
    int Height = 1200,
    double SpacingPercent = 5,   // gap between photos (UI slider 0-25); 25 = 5% of the shorter canvas side
    double RadiusPercent = 0,    // photo corner radius, % of the shorter cell side
    string Background = "#FFFFFF",
    IReadOnlyDictionary<string, PhotoAdjust>? Adjustments = null); // per photo file path

/// <summary>
/// How a photo sits inside its cell. Zoom 1 = just covers the cell; larger zooms in.
/// Pan moves the photo, in fractions of the cell size (0.1 = 10% of the cell width/height to the right/down). It is clamped so the cell stays filled.
/// </summary>
public record PhotoAdjust(double Zoom = 1, double PanX = 0, double PanY = 0)
{
    public const double MaxZoom = 4;
}

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

        for (int i = 0; i < o.Layout.Count; i++)
        {
            var cell = CellRect(o, i);
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
            PhotoAdjust? adjust = null;
            o.Adjustments?.TryGetValue(photos[i], out adjust);
            using var fitted = Cover(photo, cell.Size, adjust);
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

    /// <summary>The cell's rectangle in canvas pixels (including the spacing and outer margin).</summary>
    public static Rectangle CellRect(CollageOptions o, int index)
    {
        float gap = (float)(Math.Min(o.Width, o.Height) * o.SpacingPercent / 100.0 * 0.2);
        var c = o.Layout.Cells[index];
        // Gap between cells, and the same gap as outer margin.
        float left = c.Left * o.Width, top = c.Top * o.Height, right = c.Right * o.Width, bottom = c.Bottom * o.Height;
        left += c.Left <= 0.0001f ? gap : gap / 2;
        top += c.Top <= 0.0001f ? gap : gap / 2;
        right -= c.Right >= 0.9999f ? gap : gap / 2;
        bottom -= c.Bottom >= 0.9999f ? gap : gap / 2;
        return Rectangle.Round(RectangleF.FromLTRB(left, top, right, bottom));
    }

    /// <summary>How far (in cell fractions) the photo can be panned each way before the cell would show a gap.</summary>
    public (double X, double Y) PanLimit(CollageOptions o, int cellIndex, string photo, double zoom)
    {
        var img = Load(photo);
        var cell = CellRect(o, cellIndex);
        if (img == null || cell.Width < 2 || cell.Height < 2) return (0, 0);
        double scale = Math.Max((double)cell.Width / img.Width, (double)cell.Height / img.Height) * zoom;
        return (Math.Max(0, (img.Width * scale - cell.Width) / 2 / cell.Width),
                Math.Max(0, (img.Height * scale - cell.Height) / 2 / cell.Height));
    }

    static Bitmap Cover(Bitmap src, Size size, PhotoAdjust? adjust)
    {
        double zoom = Math.Clamp(adjust?.Zoom ?? 1, 1, PhotoAdjust.MaxZoom);
        double scale = Math.Max((double)size.Width / src.Width, (double)size.Height / src.Height) * zoom;
        float srcW = (float)(size.Width / scale), srcH = (float)(size.Height / scale);
        // Panning right shows the part of the photo further left, hence the minus.
        double centerX = src.Width / 2.0 - (adjust?.PanX ?? 0) * size.Width / scale;
        double centerY = src.Height / 2.0 - (adjust?.PanY ?? 0) * size.Height / scale;
        var srcRect = new RectangleF(
            (float)Math.Clamp(centerX - srcW / 2, 0, Math.Max(0, src.Width - srcW)),
            (float)Math.Clamp(centerY - srcH / 2, 0, Math.Max(0, src.Height - srcH)),
            srcW, srcH);
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
