using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.VisualBasic.FileIO;

namespace PixPolish;

/// <summary>Shared logic used by both the console app and the WinUI app.</summary>
public static class ImageTools
{
    public const string RoundedPostfix = "-rounded";

    public static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" };

    public static IEnumerable<string> FindImages(string dir) =>
        Directory.EnumerateFiles(dir).Where(f => ImageExtensions.Contains(Path.GetExtension(f)));

    /// <summary>Rounds the corners of every image in <paramref name="dir"/>. Returns the number of images processed.</summary>
    public static int Round(string dir, double percent, bool overwrite, Action<string> log)
    {
        var files = FindImages(dir)
            .Where(f => !Path.GetFileNameWithoutExtension(f).EndsWith(RoundedPostfix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        log($"Scanning {dir}: {files.Count} image(s), radius {percent}%{(overwrite ? ", OVERWRITE" : "")}");

        int done = 0;
        Parallel.ForEach(files, file =>
        {
            try
            {
                // Output is always PNG so the transparent corners are preserved.
                string output = Path.Combine(dir, Path.GetFileNameWithoutExtension(file) + (overwrite ? "" : RoundedPostfix) + ".png");
                using (var source = new Bitmap(file))
                using (var image = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(image))
                        g.DrawImage(source, 0, 0, source.Width, source.Height);
                    RoundCorners(image, Math.Max(2, Math.Min(image.Width, image.Height) * percent / 100.0));
                    // Save to a temp file first so the original is only replaced once the new image is complete.
                    string temp = output + ".tmp";
                    image.Save(temp, ImageFormat.Png);
                    source.Dispose();
                    File.Move(temp, output, overwrite);
                }
                // Original wasn't a PNG: its replacement has a new extension, so remove the old file.
                if (overwrite && !string.Equals(file, output, StringComparison.OrdinalIgnoreCase)) File.Delete(file);
                Interlocked.Increment(ref done);
                log($"  {Path.GetFileName(file)} -> {Path.GetFileName(output)}");
            }
            catch (Exception ex)
            {
                log($"  {Path.GetFileName(file)} FAILED: {ex.Message}");
            }
        });

        log($"Done: {done}/{files.Count}");
        return done;
    }

    /// <summary>
    /// Deletes (to the Recycle Bin) images in <paramref name="dir"/> not referenced by the markdown file(s).
    /// Returns the unused images, or null when no markdown file could be used.
    /// </summary>
    public static List<string>? Cleanup(string dir, string? markdownFile, bool dryRun, Action<string> log)
    {
        var markdownFiles = ResolveMarkdownFiles(dir, markdownFile, log);
        if (markdownFiles == null) return null;

        var used = new HashSet<string>(CollectReferences(markdownFiles).Select(r => r.FullPath), StringComparer.OrdinalIgnoreCase);

        var images = FindImages(dir).ToList();
        var unused = images.Where(f => !used.Contains(f)).ToList();

        log($"Markdown: {string.Join(", ", markdownFiles.Select(Path.GetFileName))}");
        log($"{images.Count} image(s) in folder, {images.Count - unused.Count} used, {unused.Count} unused{(dryRun ? " (dry run)" : "")}");
        foreach (var f in unused)
        {
            if (!dryRun) FileSystem.DeleteFile(f, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            log($"  {(dryRun ? "would delete" : "deleted")}: {Path.GetFileName(f)}");
        }
        return unused;
    }

    /// <summary>An image link found in a markdown file whose target file does not exist.</summary>
    public record MissingImage(string Markdown, string Link, int Line);

    /// <summary>
    /// Reports images that are referenced in the markdown file(s) but don't exist on disk.
    /// Returns the missing references, or null when no markdown file could be used.
    /// </summary>
    public static List<MissingImage>? FindMissing(string dir, string? markdownFile, Action<string> log)
    {
        var markdownFiles = ResolveMarkdownFiles(dir, markdownFile, log);
        if (markdownFiles == null) return null;

        var refs = CollectReferences(markdownFiles).ToList();
        var missing = refs.Where(r => !File.Exists(r.FullPath))
            .Select(r => new MissingImage(Path.GetFileName(r.Markdown), r.Link, r.Line))
            .Distinct().ToList();

        log($"Markdown: {string.Join(", ", markdownFiles.Select(Path.GetFileName))}");
        log($"{refs.Count} local image reference(s), {missing.Count} missing");
        foreach (var m in missing)
            log($"  missing: {m.Link}  ({m.Markdown}, line {m.Line})");
        return missing;
    }

    static List<string>? ResolveMarkdownFiles(string dir, string? markdownFile, Action<string> log)
    {
        var markdownFiles = !string.IsNullOrWhiteSpace(markdownFile)
            ? new List<string> { Path.GetFullPath(markdownFile, dir) }
            : Directory.EnumerateFiles(dir, "*.md").ToList();

        if (markdownFiles.Count == 0 || markdownFiles.Any(m => !File.Exists(m)))
        {
            log(markdownFiles.Count == 0 ? $"No markdown file found in {dir}." : $"Markdown file not found: {markdownFiles[0]}");
            return null;
        }
        return markdownFiles;
    }

    // ![alt](path "title"), ![alt](<path with spaces>), <img src="path">, [id]: path
    static readonly Regex[] ReferencePatterns =
    {
        new(@"!\[[^\]]*\]\(\s*<([^>]+)>", RegexOptions.Compiled),
        new(@"!\[[^\]]*\]\(\s*([^)\s]+)", RegexOptions.Compiled),
        new(@"<img\b[^>]*?\bsrc\s*=\s*[""']([^""']+)[""']", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"^\s*\[[^\]]+\]:\s*<?([^\s>]+)", RegexOptions.Compiled | RegexOptions.Multiline),
    };

    /// <summary>Local (non-URL) image links in the markdown files, resolved relative to each markdown file.</summary>
    static IEnumerable<(string Markdown, string Link, int Line, string FullPath)> CollectReferences(IEnumerable<string> markdownFiles)
    {
        foreach (var md in markdownFiles)
        {
            string text = File.ReadAllText(md);
            string mdDir = Path.GetDirectoryName(md)!;
            foreach (var m in ReferencePatterns.SelectMany(r => r.Matches(text)))
            {
                string raw = m.Groups[1].Value;
                string link = raw.Split('#', '?')[0];
                if (link.Length == 0 || link.Contains("://") || link.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
                if (!ImageExtensions.Contains(Path.GetExtension(link))) continue; // e.g. [id]: other.md
                string full;
                try { full = Path.GetFullPath(Uri.UnescapeDataString(link).Replace('/', Path.DirectorySeparatorChar), mdDir); }
                catch (ArgumentException) { continue; /* not a valid path */ }
                int line = text.AsSpan(0, m.Groups[1].Index).Count('\n') + 1;
                yield return (md, raw, line, full);
            }
        }
    }

    static void RoundCorners(Bitmap bmp, double r)
    {
        int w = bmp.Width, h = bmp.Height;
        int cr = (int)Math.Ceiling(r) + 1;
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[Math.Abs(data.Stride)];
            for (int y = 0; y < h; y++)
            {
                if (y >= cr && y < h - cr) continue;
                IntPtr ptr = data.Scan0 + y * data.Stride;
                Marshal.Copy(ptr, row, 0, w * 4);
                for (int x = 0; x < w; x++)
                {
                    if (x >= cr && x < w - cr) { x = w - cr - 1; continue; }
                    double cx = x < cr ? r : w - r;
                    double cy = y < cr ? r : h - r;
                    int inside = 0;
                    // Anti-aliased coverage via 4x4 supersampling against the corner circle.
                    for (int sy = 0; sy < 4; sy++)
                        for (int sx = 0; sx < 4; sx++)
                        {
                            double px = x + (sx + 0.5) / 4, py = y + (sy + 0.5) / 4;
                            bool inCorner = (px < r || px > w - r) && (py < r || py > h - r);
                            if (!inCorner || (px - cx) * (px - cx) + (py - cy) * (py - cy) <= r * r) inside++;
                        }
                    if (inside == 16) continue;
                    row[x * 4 + 3] = (byte)(row[x * 4 + 3] * inside / 16);
                }
                Marshal.Copy(row, 0, ptr, w * 4);
            }
        }
        finally { bmp.UnlockBits(data); }
    }
}
