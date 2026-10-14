using Microsoft.UI.Xaml;
using Microsoft.Windows.Storage.Pickers;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace PixPolish.UI;

// Picking or dropping individual images (and folders) instead of browsing for a folder.
public sealed partial class MainWindow
{
    List<string> _roundFiles = new();

    void SetRoundFiles(IEnumerable<string> files)
    {
        _roundFiles = files.ToList();
        RoundSelectionText.Text = _roundFiles.Count == 0 ? "" :
            _roundFiles.Count == 1 ? $"1 image: {Path.GetFileName(_roundFiles[0])}" : $"{_roundFiles.Count} images selected";
    }

    async void PickRoundImages_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker(AppWindow.Id);
            foreach (var ext in ImageTools.ImageExtensions) picker.FileTypeFilter.Add(ext);
            var files = await picker.PickMultipleFilesAsync();
            if (files != null && files.Count > 0) SetRoundFiles(files.Select(f => f.Path));
        }
        catch (Exception ex) { Log("Image picker failed: " + ex.Message); }
    }

    void ClearRoundImages_Click(object sender, RoutedEventArgs e) => SetRoundFiles(Array.Empty<string>());

    void Window_DragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = ToolPivot.SelectedIndex == 3 ? "Add to collage" : "Drop images or a folder";
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
                SetRoundFiles(images);
                ToolPivot.SelectedIndex = 0; // dropped images go to the round tool
            }
            else if (folder != null)
            {
                SetRoundFiles(Array.Empty<string>());
                FolderBox.Text = folder.Path;
            }
            else Log("Drop image files (png, jpg, bmp, gif, tif) or a folder.");
        }
        catch (Exception ex) { Log("Drop failed: " + ex.Message); }
        finally { deferral.Complete(); }
    }
}
