using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;

namespace PixPolish.UI;

public sealed partial class MainWindow : Window
{
    const string AllMarkdown = "(all *.md files in the folder)";

    public MainWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1100, 860));
        try { AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "PixPolish.ico")); } catch { /* icon is cosmetic */ }
        InitRound();
        InitCollage();
    }

    async void Browse_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // The WindowsAppSDK picker is the reliable one in unpackaged apps.
            var picker = new FolderPicker(AppWindow.Id);
            var folder = await picker.PickSingleFolderAsync();
            if (folder != null) FolderBox.Text = folder.Path;
        }
        catch (Exception ex) { Log("Folder picker failed: " + ex.Message + " - type the path instead."); }
    }

    void FolderBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        foreach (var combo in new[] { MarkdownCombo, MissingCombo })
        {
            combo.Items.Clear();
            combo.Items.Add(AllMarkdown);
            if (Directory.Exists(FolderBox.Text))
                foreach (var md in Directory.EnumerateFiles(FolderBox.Text, "*.md"))
                    combo.Items.Add(Path.GetFileName(md));
            combo.SelectedIndex = 0;
        }
        LoadPhotosFromFolder(FolderBox.Text);
        LoadRoundFromFolder(FolderBox.Text);
    }

    void Log(string line) => DispatcherQueue.TryEnqueue(() => LogBox.Text += line + "\n");

    async void Run_Click(object sender, RoutedEventArgs e)
    {
        string dir = FolderBox.Text;
        bool isRound = ToolPivot.SelectedIndex == 0; // the round tool works on the thumbnail list, not on the folder
        if (isRound && _roundItems.Count == 0) { Log("No images in the list."); return; }
        if (!isRound && !Directory.Exists(dir)) { Log("Pick an existing folder first."); return; }

        LogBox.Text = "";
        RunButton.IsEnabled = false;
        try
        {
            if (ToolPivot.SelectedIndex == 0)
            {
                double percent = RadiusBox.Value;
                bool overwrite = PostfixCheck.IsChecked != true; // originals are replaced unless the postfix option is ticked
                var files = _roundItems.Select(i => i.Path).ToList();
                await Task.Run(() => ImageTools.RoundFiles(files, percent, overwrite, Log));
                if (overwrite) RefreshRoundItemsAfterOverwrite();
            }
            else if (ToolPivot.SelectedIndex == 3)
            {
                await SaveCollage(dir);
            }
            else if (ToolPivot.SelectedIndex == 2)
            {
                string? md = MissingCombo.SelectedItem as string;
                if (md == AllMarkdown) md = null;
                await Task.Run(() => ImageTools.FindMissing(dir, md, Log));
            }
            else
            {
                string? md = MarkdownCombo.SelectedItem as string;
                if (md == AllMarkdown) md = null;
                bool dryRun = DryRunCheck.IsChecked == true;

                // Always preview first; only delete after the user confirms.
                var unused = await Task.Run(() => ImageTools.Cleanup(dir, md, true, dryRun ? Log : _ => { }));
                if (unused == null) { await Task.Run(() => ImageTools.Cleanup(dir, md, true, Log)); return; }
                if (dryRun) return;
                if (unused.Count == 0) { Log("No unused images."); return; }
                if (!await Confirm($"Delete {unused.Count} unused image(s)?", string.Join("\n", unused.Take(15).Select(Path.GetFileName)) + (unused.Count > 15 ? "\n..." : "") + "\n\nThey will be moved to the Recycle Bin.")) return;
                await Task.Run(() => ImageTools.Cleanup(dir, md, false, Log));
            }
        }
        catch (Exception ex) { Log("Error: " + ex.Message); }
        finally { RunButton.IsEnabled = true; }
    }

    async Task<bool> Confirm(string title, string text)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = title,
            Content = text,
            PrimaryButtonText = "Continue",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
