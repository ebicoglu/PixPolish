using System.Globalization;
using PixPolish;

// Usage:
//   PixPolish round   [radiusPercent] [--overwrite]   round image corners (default radius 3% of the shorter side)
//   PixPolish cleanup [file.md] [--dry-run]           delete images not referenced by the markdown file(s)
string dir = Directory.GetCurrentDirectory();
string command = args.Length > 0 ? args[0].ToLowerInvariant() : "";
string[] opts = args.Skip(1).ToArray();

switch (command)
{
    case "round":
        double percent = opts.Select(a => double.TryParse(a, CultureInfo.InvariantCulture, out var v) ? v : (double?)null)
            .FirstOrDefault(v => v != null) ?? 3;
        bool overwrite = opts.Any(a => a is "--overwrite" or "-o");
        // Image files named on the command line are rounded instead of the whole folder.
        var named = opts.Where(a => !a.StartsWith('-') && !double.TryParse(a, CultureInfo.InvariantCulture, out _))
            .Select(a => Path.GetFullPath(a, dir)).ToList();
        if (named.Count == 0) ImageTools.Round(dir, percent, overwrite, Console.WriteLine);
        else
        {
            var missingFiles = named.Where(f => !File.Exists(f)).ToList();
            foreach (var f in missingFiles) Console.WriteLine($"Not found: {f}");
            ImageTools.RoundFiles(named.Except(missingFiles).ToList(), percent, overwrite, Console.WriteLine);
            if (missingFiles.Count > 0) return 1;
        }
        return 0;
    case "cleanup":
        var unused = ImageTools.Cleanup(dir, opts.FirstOrDefault(a => !a.StartsWith('-')), opts.Any(a => a is "--dry-run" or "-n"), Console.WriteLine);
        return unused == null ? 1 : 0;
    case "collage":
        return RunCollage(opts);
    case "missing":
        var missing = ImageTools.FindMissing(dir, opts.FirstOrDefault(a => !a.StartsWith('-')), Console.WriteLine);
        return missing == null ? 1 : missing.Count > 0 ? 2 : 0; // exit code 2 = missing images found
    default:
        Console.WriteLine("""
            PixPolish <command> [options]

              round   [files...] [radiusPercent] [--overwrite]  Round image corners (the given files, or all in the current folder).
                                                     Default radius 3 (% of shorter side). Saves <name>-rounded.png,
                                                     or replaces the original with --overwrite.
              cleanup [file.md] [--dry-run]          Delete (to Recycle Bin) images in the current folder that are not
                                                     referenced by file.md (or by all *.md files if none is given).
              missing [file.md]                      Report images referenced in the markdown but missing on disk.
              collage [photos...] [options]          Combine photos into one collage (default: the images in the folder).
                --layout <n|name>  --list  --spacing 0-25  --radius %  --bg #RRGGBB|transparent  --size WxH  --out file.png
            """);
        return command == "" ? 0 : 1;
}

int RunCollage(string[] o)
{
    if (o.Contains("--list"))
    {
        for (int i = 0; i < CollageLayouts.All.Count; i++)
            Console.WriteLine($"{i + 1,3}. {CollageLayouts.All[i].Name} ({CollageLayouts.All[i].Count} photos)");
        return 0;
    }

    string? Opt(string name) { int i = Array.IndexOf(o, name); return i >= 0 && i + 1 < o.Length ? o[i + 1] : null; }
    var optionValues = new HashSet<string>(new[] { "--layout", "--spacing", "--radius", "--bg", "--size", "--out" }
        .Select(Opt).Where(v => v != null)!);

    var photos = o.Where(a => !a.StartsWith("--") && !optionValues.Contains(a)).Select(a => Path.GetFullPath(a, dir)).ToList();
    if (photos.Count == 0)
        photos = ImageTools.FindImages(dir)
            .Where(f => !Path.GetFileName(f).StartsWith("collage", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
    if (photos.Count == 0) { Console.WriteLine("No photos found."); return 1; }

    CollageLayout? layout;
    if (Opt("--layout") is { } key)
    {
        layout = CollageLayouts.Find(key);
        if (layout == null) { Console.WriteLine($"Unknown layout '{key}'. Use --list."); return 1; }
    }
    else // the layout that fits the photo count best
        layout = CollageLayouts.All.FirstOrDefault(l => l.Count == photos.Count)
              ?? CollageLayouts.All.Where(l => l.Count <= photos.Count).MaxBy(l => l.Count)!;

    int w = 1600, h = 1200;
    if (Opt("--size") is { } size)
    {
        var p = size.ToLowerInvariant().Split('x');
        if (p.Length != 2 || !int.TryParse(p[0], out w) || !int.TryParse(p[1], out h) || w < 10 || h < 10)
        { Console.WriteLine("Invalid --size, expected e.g. 1600x1200."); return 1; }
    }
    double Num(string name, double fallback) => double.TryParse(Opt(name), CultureInfo.InvariantCulture, out var v) ? v : fallback;

    var options = new CollageOptions(layout, w, h, Num("--spacing", 5), Num("--radius", 0), Opt("--bg") ?? "#FFFFFF");
    string output = Path.GetFullPath(Opt("--out") ?? "collage.png", dir);

    using var renderer = new CollageRenderer();
    using var bmp = renderer.Render(options, photos);
    bmp.Save(output, System.Drawing.Imaging.ImageFormat.Png);
    Console.WriteLine($"Layout '{layout.Name}', {Math.Min(photos.Count, layout.Count)}/{layout.Count} cell(s) filled -> {output}");
    return 0;
}
