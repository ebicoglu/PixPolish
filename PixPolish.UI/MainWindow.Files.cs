using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace PixPolish.UI;

// The "Round corners" image list (thumbnails you can remove from) and picking / dropping images or folders.
public sealed partial class MainWindow
{
    readonly ObservableCollection<PhotoItem> _roundItems = new();

    void InitRound()
    {
        RoundGrid.ItemsSource = _roundItems;
        _roundItems.CollectionChanged += (_, _) =>
            RoundSelectionText.Text = _roundItems.Count == 0
                ? "No images - add some, drop them here, or pick a folder."
                : $"{_roundItems.Count} image(s) will be rounded";
        RoundSelectionText.Text = "No images - add some, drop them here, or pick a folder.";
    }

    void AddRoundFiles(IEnumerable<string> files)
    {
        var known = new HashSet<string>(_roundItems.Select(i => i.Path), StringComparer.OrdinalIgnoreCase);
        foreach (var f in files)
            if (known.Add(f)) _roundItems.Add(new PhotoItem(f));
    }

    void LoadRoundFromFolder(string dir)
    {
        _roundItems.Clear();
        if (!Directory.Exists(dir)) return;
        AddRoundFiles(ImageTools.FindImages(dir)
            .Where(f => !Path.GetFileNameWithoutExtension(f).EndsWith(ImageTools.RoundedPostfix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>After overwriting, JPG/BMP/... originals have become PNGs: point the list at the new files so thumbnails stay valid.</summary>
    void RefreshRoundItemsAfterOverwrite()
    {
        for (int i = 0; i < _roundItems.Count; i++)
        {
            string png = Path.ChangeExtension(_roundItems[i].Path, ".png");
            if (File.Exists(png)) _roundItems[i] = new PhotoItem(png);
        }
    }

    async void PickRoundImages_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker(AppWindow.Id);
            foreach (var ext in ImageTools.ImageExtensions) picker.FileTypeFilter.Add(ext);
            var files = await picker.PickMultipleFilesAsync();
            if (files != null && files.Count > 0) AddRoundFiles(files.Select(f => f.Path));
        }
        catch (Exception ex) { Log("Image picker failed: " + ex.Message); }
    }

    void ClearRoundImages_Click(object sender, RoutedEventArgs e) => _roundItems.Clear();

    void ReloadRoundFolder_Click(object sender, RoutedEventArgs e) => LoadRoundFromFolder(FolderBox.Text);

    void RemoveRoundItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is PhotoItem item) _roundItems.Remove(item);
    }

    void Window_DragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = ToolPivot.SelectedIndex == 3 ? "Add to collage" : "Add images or a folder";
    }

    async void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return; // e.g. a photo dragged within the collage list
        var deferral = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var images = items.OfType<StorageFile>().Select(f => f.Path)
                .Where(p => ImageTools.ImageExtensions.Contains(Path.GetExtension(p))).ToList();
            var folder = items.OfType<StorageFolder>().FirstOrDefault();

            if (images.Count > 0 && ToolPivot.SelectedIndex == 3)
            {
                foreach (var p in images) _photos.Add(new PhotoItem(p)); // collage: add the dropped photos
            }
            else if (images.Count > 0)
            {
                AddRoundFiles(images);
                ToolPivot.SelectedIndex = 0; // dropped images go to the round tool
            }
            else if (folder != null)
            {
                bool sameFolder = string.Equals(FolderBox.Text, folder.Path, StringComparison.OrdinalIgnoreCase);
                FolderBox.Text = folder.Path;              // TextChanged loads the folder's images...
                if (sameFolder) LoadRoundFromFolder(folder.Path); // ...unless nothing changed
            }
            else Log("Drop image files (png, jpg, bmp, gif, tif) or a folder.");
        }
        catch (Exception ex) { Log("Drop failed: " + ex.Message); }
        finally { deferral.Complete(); }
    }
}
