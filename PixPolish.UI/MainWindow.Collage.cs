using System.Collections.ObjectModel;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Rectangle = Microsoft.UI.Xaml.Shapes.Rectangle;
using Microsoft.Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace PixPolish.UI;

public sealed class PhotoItem
{
    public PhotoItem(string path)
    {
        Path = path;
        // Decoding at thumbnail size keeps big photos cheap.
        Thumbnail = new BitmapImage { DecodePixelWidth = 144, UriSource = new Uri(path) };
    }
    public string Path { get; }
    public BitmapImage Thumbnail { get; }
    public string Name => System.IO.Path.GetFileName(Path);
}

// Collage tab: layout gallery, live preview, photo list.
public sealed partial class MainWindow
{
    static readonly (string Name, double W, double H)[] Aspects =
        { ("Landscape 4:3", 4, 3), ("Landscape 3:2", 3, 2), ("Wide 16:9", 16, 9), ("Square 1:1", 1, 1), ("Portrait 3:4", 3, 4) };

    static readonly (string Name, string Color)[] Backgrounds =
        { ("White", "#FFFFFF"), ("Light gray", "#E8EAEC"), ("Black", "#000000"), ("Transparent", "transparent") };

    readonly ObservableCollection<PhotoItem> _photos = new();
    readonly CollageRenderer _previewRenderer = new(maxSide: 900);
    readonly object _previewLock = new();
    bool _collageReady;
    int _previewVersion;

    void InitCollage()
    {
        foreach (var layout in CollageLayouts.All)
        {
            var thumb = new Canvas { Width = 56, Height = 44 };
            foreach (var c in layout.Cells)
            {
                var r = new Rectangle
                {
                    Width = Math.Max(1, c.Width * 56 - 2),
                    Height = Math.Max(1, c.Height * 44 - 2),
                    Fill = new SolidColorBrush(Colors.SlateGray),
                };
                Canvas.SetLeft(r, c.X * 56 + 1);
                Canvas.SetTop(r, c.Y * 44 + 1);
                thumb.Children.Add(r);
            }
            ToolTipService.SetToolTip(thumb, $"{layout.Name} ({layout.Count})");
            LayoutGrid.Items.Add(thumb);
        }
        LayoutGrid.SelectedIndex = 4; // two on top, one below

        foreach (var a in Aspects) AspectCombo.Items.Add(a.Name);
        AspectCombo.SelectedIndex = 0;
        foreach (var b in Backgrounds) BackgroundCombo.Items.Add(b.Name);
        BackgroundCombo.SelectedIndex = 0;

        PhotoList.ItemsSource = _photos;
        _photos.CollectionChanged += (_, _) => { UpdatePhotosHeader(); ClampSelection(); _ = RefreshPreview(); };

        _collageReady = true;
        _ = RefreshPreview();
    }

    void LoadPhotosFromFolder(string dir)
    {
        _photos.Clear();
        if (!Directory.Exists(dir)) return;
        foreach (var f in ImageTools.FindImages(dir)
                     .Where(f => !Path.GetFileName(f).StartsWith("collage", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            _photos.Add(new PhotoItem(f));
    }

    void UpdatePhotosHeader()
    {
        int cells = (LayoutGrid.SelectedIndex >= 0 ? CollageLayouts.All[LayoutGrid.SelectedIndex].Count : 0);
        PhotosHeader.Text = $"Photos: {_photos.Count} (layout uses {cells}) - drag to reorder";
    }

    void CollageOption_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_collageReady) return;
        UpdatePhotosHeader();
        ClampSelection();
        _ = RefreshPreview();
    }

    void CollageSlider_Changed(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_collageReady) _ = RefreshPreview();
    }

    async void ChoosePhotos_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker(AppWindow.Id);
            foreach (var ext in ImageTools.ImageExtensions) picker.FileTypeFilter.Add(ext);
            var files = await picker.PickMultipleFilesAsync();
            if (files == null || files.Count == 0) return;
            _photos.Clear();
            foreach (var f in files) _photos.Add(new PhotoItem(f.Path));
        }
        catch (Exception ex) { Log("Photo picker failed: " + ex.Message); }
    }

    void RemovePhoto_Click(object sender, RoutedEventArgs e)
    {
        if (PhotoList.SelectedItem is PhotoItem p) _photos.Remove(p);
    }

    (CollageOptions Options, int LongSide) CurrentOptions(int longSide)
    {
        var layout = CollageLayouts.All[Math.Max(0, LayoutGrid.SelectedIndex)];
        var (_, rw, rh) = Aspects[Math.Max(0, AspectCombo.SelectedIndex)];
        int w = rw >= rh ? longSide : (int)(longSide * rw / rh);
        int h = rw >= rh ? (int)(longSide * rh / rw) : longSide;
        string bg = Backgrounds[Math.Max(0, BackgroundCombo.SelectedIndex)].Color;
        var adjustments = new Dictionary<string, PhotoAdjust>(_adjust, StringComparer.OrdinalIgnoreCase);
        return (new CollageOptions(layout, w, h, SpacingSlider.Value, CornerSlider.Value, bg, adjustments), longSide);
    }

    async Task RefreshPreview()
    {
        int version = ++_previewVersion;
        var options = CurrentOptions(800).Options;
        var photos = _photos.Select(p => p.Path).ToList();
        byte[] png;
        try { png = await Task.Run(() => { lock (_previewLock) return _previewRenderer.RenderPng(options, photos); }); }
        catch (Exception ex) { Log("Preview failed: " + ex.Message); return; }
        if (version != _previewVersion) return; // a newer render is already on its way

        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(png.AsBuffer());
        stream.Seek(0);
        var image = new BitmapImage();
        await image.SetSourceAsync(stream);
        PreviewImage.Source = image;
        UpdateSelectionBox();
    }

    // ---- Resizing / moving a photo inside its cell ----

    readonly Dictionary<string, PhotoAdjust> _adjust = new(StringComparer.OrdinalIgnoreCase);
    int _selectedCell = -1;
    bool _dragging, _suppressZoomEvent;
    Windows.Foundation.Point _lastPoint;

    // Where the (uniformly stretched) preview image sits inside PreviewHost, and its scale relative to canvas pixels.
    (double Scale, double OffX, double OffY, CollageOptions Options) PreviewGeometry()
    {
        var options = CurrentOptions(800).Options;
        double scale = Math.Min(PreviewHost.ActualWidth / options.Width, PreviewHost.ActualHeight / options.Height);
        if (double.IsNaN(scale) || scale <= 0) scale = 1;
        return (scale, (PreviewHost.ActualWidth - options.Width * scale) / 2, (PreviewHost.ActualHeight - options.Height * scale) / 2, options);
    }

    int CellAt(Windows.Foundation.Point p, (double Scale, double OffX, double OffY, CollageOptions Options) g)
    {
        var canvasPoint = new System.Drawing.Point((int)((p.X - g.OffX) / g.Scale), (int)((p.Y - g.OffY) / g.Scale));
        for (int i = 0; i < Math.Min(g.Options.Layout.Count, _photos.Count); i++)
            if (CollageRenderer.CellRect(g.Options, i).Contains(canvasPoint)) return i;
        return -1;
    }

    void SelectCell(int cell)
    {
        _selectedCell = cell;
        _suppressZoomEvent = true;
        ZoomSlider.Value = cell >= 0 && _adjust.TryGetValue(_photos[cell].Path, out var a) ? a.Zoom : 1;
        _suppressZoomEvent = false;
        ZoomSlider.IsEnabled = ResetPhotoButton.IsEnabled = cell >= 0;
        UpdateSelectionBox();
    }

    void ClampSelection()
    {
        if (_selectedCell >= Math.Min(_photos.Count, CollageLayouts.All[Math.Max(0, LayoutGrid.SelectedIndex)].Count)) SelectCell(-1);
        else UpdateSelectionBox();
    }

    void UpdateSelectionBox()
    {
        if (_selectedCell < 0 || _selectedCell >= _photos.Count || _selectedCell >= CollageLayouts.All[Math.Max(0, LayoutGrid.SelectedIndex)].Count)
        {
            SelectionBox.Visibility = Visibility.Collapsed;
            return;
        }
        var g = PreviewGeometry();
        var cell = CollageRenderer.CellRect(g.Options, _selectedCell);
        Canvas.SetLeft(SelectionBox, g.OffX + cell.X * g.Scale);
        Canvas.SetTop(SelectionBox, g.OffY + cell.Y * g.Scale);
        SelectionBox.Width = cell.Width * g.Scale;
        SelectionBox.Height = cell.Height * g.Scale;
        SelectionBox.Visibility = Visibility.Visible;
    }

    // Drag a photo from the list onto a cell of the preview: it swaps places with the photo that is in that cell.
    PhotoItem? _draggedPhoto;

    void PhotoList_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        _draggedPhoto = e.Items.FirstOrDefault() as PhotoItem;
        e.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
    }

    void Preview_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = _draggedPhoto != null
            ? Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move
            : Windows.ApplicationModel.DataTransfer.DataPackageOperation.None;
        e.DragUIOverride.Caption = "Place here";
    }

    void Preview_Drop(object sender, DragEventArgs e)
    {
        if (_draggedPhoto == null) return;
        var g = PreviewGeometry();
        var p = e.GetPosition(PreviewHost);
        var canvasPoint = new System.Drawing.Point((int)((p.X - g.OffX) / g.Scale), (int)((p.Y - g.OffY) / g.Scale));
        int from = _photos.IndexOf(_draggedPhoto);
        for (int cell = 0; cell < g.Options.Layout.Count && from >= 0; cell++)
        {
            if (!CollageRenderer.CellRect(g.Options, cell).Contains(canvasPoint)) continue;
            int to = Math.Min(cell, _photos.Count - 1);
            if (to != from)
            {
                var other = _photos[to];
                _photos[to] = _draggedPhoto;
                _photos[from] = other;
            }
            SelectCell(to);
            break;
        }
        _draggedPhoto = null;
    }

    void PreviewHost_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateSelectionBox();

    /// <summary>Sets zoom/pan for the selected photo, keeping the photo covering its cell.</summary>
    void ApplyAdjust(int cell, double zoom, double panX, double panY)
    {
        var options = CurrentOptions(800).Options;
        string path = _photos[cell].Path;
        zoom = Math.Clamp(zoom, 1, PhotoAdjust.MaxZoom);
        (double limX, double limY) = (0, 0);
        lock (_previewLock) (limX, limY) = _previewRenderer.PanLimit(options, cell, path, zoom);
        _adjust[path] = new PhotoAdjust(zoom, Math.Clamp(panX, -limX, limX), Math.Clamp(panY, -limY, limY));
        _ = RefreshPreview();
    }

    void Preview_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(PreviewHost).Position;
        SelectCell(CellAt(p, PreviewGeometry()));
        if (_selectedCell < 0) return;
        _dragging = true;
        _lastPoint = p;
        PreviewHost.CapturePointer(e.Pointer);
    }

    void Preview_PointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_dragging || _selectedCell < 0 || _selectedCell >= _photos.Count) return;
        var p = e.GetCurrentPoint(PreviewHost).Position;
        var g = PreviewGeometry();
        var cell = CollageRenderer.CellRect(g.Options, _selectedCell);
        var current = _adjust.GetValueOrDefault(_photos[_selectedCell].Path) ?? new PhotoAdjust();
        // Pan is stored in cell fractions, so convert the mouse movement from screen pixels.
        ApplyAdjust(_selectedCell, current.Zoom,
            current.PanX + (p.X - _lastPoint.X) / (g.Scale * cell.Width),
            current.PanY + (p.Y - _lastPoint.Y) / (g.Scale * cell.Height));
        _lastPoint = p;
    }

    void Preview_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _dragging = false;
        PreviewHost.ReleasePointerCaptures();
    }

    void Preview_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(PreviewHost);
        int cell = CellAt(point.Position, PreviewGeometry());
        if (cell < 0) return;
        if (cell != _selectedCell) SelectCell(cell);
        var current = _adjust.GetValueOrDefault(_photos[cell].Path) ?? new PhotoAdjust();
        double zoom = current.Zoom * Math.Pow(1.1, point.Properties.MouseWheelDelta / 120.0);
        ApplyAdjust(cell, zoom, current.PanX, current.PanY);
        _suppressZoomEvent = true;
        ZoomSlider.Value = Math.Clamp(zoom, 1, PhotoAdjust.MaxZoom);
        _suppressZoomEvent = false;
        e.Handled = true;
    }

    void ZoomSlider_Changed(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_suppressZoomEvent || _selectedCell < 0 || _selectedCell >= _photos.Count) return;
        var current = _adjust.GetValueOrDefault(_photos[_selectedCell].Path) ?? new PhotoAdjust();
        ApplyAdjust(_selectedCell, e.NewValue, current.PanX, current.PanY);
    }

    void ResetPhoto_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCell < 0 || _selectedCell >= _photos.Count) return;
        _adjust.Remove(_photos[_selectedCell].Path);
        SelectCell(_selectedCell);
        _ = RefreshPreview();
    }

    /// <summary>Renders the collage at full resolution and saves it as collage.png (collage-2.png, ... if it exists).</summary>
    async Task SaveCollage(string dir)
    {
        // Read the controls on the UI thread, render on a background thread.
        var (options, _) = CurrentOptions(2400);
        var photos = _photos.Select(p => p.Path).ToList();
        if (photos.Count == 0) { Log("No photos to use."); return; }

        string output = Path.Combine(dir, "collage.png");
        for (int i = 2; File.Exists(output); i++) output = Path.Combine(dir, $"collage-{i}.png");

        await Task.Run(() =>
        {
            using var renderer = new CollageRenderer();
            using var bmp = renderer.Render(options, photos);
            bmp.Save(output, System.Drawing.Imaging.ImageFormat.Png);
        });
        Log($"Saved {output} ({options.Width}x{options.Height}, '{options.Layout.Name}', {Math.Min(photos.Count, options.Layout.Count)}/{options.Layout.Count} cells filled)");
    }
}
