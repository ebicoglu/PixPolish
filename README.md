# PixPolish

A small Windows console tool for preparing images for articles. It works on the **current folder** (the folder you run it from), not the folder where the exe lives.

- **round** – rounds image corners (transparent, no border).
- **cleanup** – deletes images that a markdown file doesn't use.
- **missing** – reports images a markdown file uses that don't exist on disk.
- **collage** – combines photos into one collage using a chosen layout.

## Projects

| Folder | What |
|---|---|
| `PixPolish.Core` | Shared logic (rounding, cleanup) used by both apps. |
| `PixPolish` | Console app (`PixPolish.exe`) plus the `.bat` helpers. |
| `PixPolish.UI` | WinUI 3 desktop app (`PixPolishUI.exe`): pick a folder, choose *Round corners*, *Clean up unused* or *Find missing images*, press Run. Overwrite and delete actions ask for confirmation; cleanup always previews before deleting. |

## Build

```bash
dotnet build PixPolish -c Release -o PixPolish\out
dotnet build PixPolish.UI -c Release -o PixPolish.UI\out\
```

The WinUI build needs the trailing `\` on its output path, otherwise the XAML files are written next to the project and the app crashes on start.

The output folder contains `PixPolish.exe` and the `.bat` helpers. A `.bat` is re-copied on build only when the source one is newer.

## Usage

```bash
PixPolish.exe round   [radiusPercent] [--overwrite]
PixPolish.exe cleanup [file.md] [--dry-run]
PixPolish.exe missing [file.md]
PixPolish.exe collage [photos...] [options]
```

Run `PixPolish.exe` with no arguments to see the help text.

### `collage`

Combines photos into one collage image (PNG).

```bash
PixPolish.exe collage [photos...] [--layout n|name] [--spacing 0-25] [--radius %] [--bg #RRGGBB|transparent] [--size WxH] [--out file.png]
PixPolish.exe collage --list
```

| Option | Meaning |
|---|---|
| `photos...` | Files to use, in order. If none are given, the images in the current folder (sorted by name, ignoring `collage*`). |
| `--layout` | Layout number or part of its name (see `--list`, 18 layouts from 1 to 9 photos). Default: the layout whose cell count matches the number of photos. |
| `--spacing` | Gap between photos and around the edge, 0-25 (25 = 5% of the shorter side). Default `5`. |
| `--radius` | Rounded photo corners, as % of the shorter cell side. Default `0`. |
| `--bg` | Background colour as `#RRGGBB`, a colour name, or `transparent`. Default `#FFFFFF`. |
| `--size` | Canvas size. Default `1600x1200`. |
| `--out` | Output file. Default `collage.png`. |

Photos fill their cell completely (scaled and centre-cropped). Extra photos are ignored; empty cells stay light gray.

In the WinUI app, the **Collage** tab shows a live preview: pick a layout, move the spacing / corner sliders, choose the shape (4:3, 3:2, 16:9, 1:1, 3:4) and background, and the photo list shows thumbnails; drag them to reorder, or drop one onto a cell of the preview to swap it into that cell. To resize the photos, **drag the white lines** between them (the line lights up and the cursor changes; a line shared by several photos, like a grid row or column, moves them all; *Reset photo sizes* restores the layout). To zoom a photo inside its cell, click it in the preview (it gets an outline), then **scroll to zoom** and **drag to move** it, or use the *Zoom selected photo* slider; *Reset selected photo* undoes it. The photo always keeps covering its cell, and zoom/position follow the photo if you reorder. *Choose photos...* replaces the list (the folder's images are the default). **Run** saves a 2400px collage as `collage.png` (or `collage-2.png`, ... without overwriting).

### `missing`

The opposite of `cleanup`: reports images that are **written in the markdown but don't exist** on disk.

```
Markdown: post.md
3 local image reference(s), 2 missing
  missing: gone.png  (post.md, line 4)
  missing: ./sub/nope.jpg  (post.md, line 5)
```

- Uses the same link detection as `cleanup`, so paths are resolved relative to the markdown file, `%20` is decoded, and `http(s)://` / `data:` images are ignored. Only links ending in an image extension count.
- Uses `file.md` if given, otherwise all `*.md` files in the folder. Nothing is changed on disk.
- Exit code: `0` none missing, `2` missing images found, `1` no markdown file / not found. Handy in scripts.

### `round`

Scans the current folder (non-recursive) for `.png .jpg .jpeg .bmp .gif .tif .tiff` and rounds the corners.

| Option | Meaning |
|---|---|
| `radiusPercent` | Corner radius as a % of the image's shorter side. Default `3`. |
| `--overwrite`, `-o` | Replace the original instead of creating a copy. |

- Default output: `photo.jpg` → `photo-rounded.png` (originals untouched).
- With `--overwrite`: `photo.png` is replaced in place; `photo.jpg` becomes `photo.png` and the `.jpg` is deleted.
- Output is always PNG, because JPG can't store transparent corners.
- Files already ending in `-rounded` are skipped, so re-running is safe.
- The result is written to a temp file first, so an original is only replaced once the new image is complete.
- Edges are anti-aliased (4×4 supersampling of the corner pixels).

### `cleanup`

Deletes images in the current folder that no markdown file references.

| Option | Meaning |
|---|---|
| `file.md` | Use only this markdown file. If omitted, all `*.md` files in the folder are used together. |
| `--dry-run`, `-n` | Only list what would be deleted. |

How it works:

1. Reads the markdown file(s) and collects image references:
   `![alt](path "title")`, `![alt](<path with spaces>)`, `<img src="path">` and `[id]: path`.
2. Resolves each path relative to the markdown file, decoding `%20`-style escapes and ignoring `#anchors`, `?queries`, `http(s)://` and `data:` links.
3. Any image file directly in the folder that isn't in that set is sent to the **Recycle Bin** (so it can be restored).

Notes:

- Only images directly in the folder are checked, not subfolders.
- If the markdown uses `photo.png` and you also ran `round` (creating `photo-rounded.png`), the `-rounded` copy counts as unused and will be deleted. Use `round --overwrite`, or reference the rounded file in the markdown.
- If no markdown file is found, nothing is deleted.
- Try `--dry-run` first.

## Batch files

Double-click-friendly wrappers placed next to `PixPolish.exe`. Run them from the folder that holds your images.

| File | Runs |
|---|---|
| `round.bat [radius]` | `PixPolish.exe round` |
| `round-overwrite.bat [radius]` | `PixPolish.exe round --overwrite` |
| `cleanup.bat [file.md]` | `PixPolish.exe cleanup` |
| `cleanup-dry-run.bat [file.md]` | `PixPolish.exe cleanup --dry-run` |
| `missing.bat [file.md]` | `PixPolish.exe missing` |
| `collage.bat [photos...] [options]` | `PixPolish.exe collage` |

Typical article workflow:

```bash
cd D:\path\to\article-folder
D:\path\to\PixPolish\out\cleanup-dry-run.bat
D:\path\to\PixPolish\out\cleanup.bat
D:\path\to\PixPolish\out\round-overwrite.bat
```

## Requirements

Windows and the .NET 10 SDK (uses `System.Drawing.Common`).

## License

[MIT](LICENSE)
