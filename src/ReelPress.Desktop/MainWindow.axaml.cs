using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using ReelPress.Desktop.ViewModels;

namespace ReelPress.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var paths = new List<string>();

        var files = e.Data.GetFiles();
        if (files is not null)
        {
            foreach (var file in files)
            {
                var localPath = file.TryGetLocalPath();
                if (!string.IsNullOrWhiteSpace(localPath))
                {
                    paths.Add(localPath);
                }
            }
        }

        if (paths.Count == 0)
        {
            var text = e.Data.GetText();
            if (!string.IsNullOrWhiteSpace(text))
            {
                paths.Add(text.Trim());
            }
        }

        if (paths.Count > 0)
        {
            viewModel.AddPaths(paths);
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var hasFilePayload = e.Data.Contains(DataFormats.Files);
        e.DragEffects = hasFilePayload ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
}
