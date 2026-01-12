using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System.Collections.ObjectModel;
using WhatHuh.Core.Models;
using WhatHuh.Core.Services;

namespace WhatHuh.Gui;

public partial class MainWindow : Window
{
    public ObservableCollection<string> FilesToConvert { get; set; } = [];
    public List<WhisperModelOption> ModelOptions { get; set; }
    public WhisperModelOption SelectedModel { get; set; }
    public string AppRootPath { get; set; }
    private CancellationTokenSource? Cts;

    public MainWindow()
    {
        InitializeComponent();

        AppRootPath = AppContext.BaseDirectory;
        ModelOptions = WhisperModelOption.GetDefaultOptions();
        SelectedModel = ModelOptions[0];

        InitializeControls();
        SetupDragDrop();
        SetupKeyboardShortcuts();
    }

    private void InitializeControls()
    {
        ModelComboBox.ItemsSource = ModelOptions.Select(m => m.DisplayName)
            .ToList();
        ModelComboBox.SelectedIndex = 0;

        FileQueueListBox.ItemsSource = FilesToConvert;

        UseLlmCheckBox.IsCheckedChanged += (s, e) =>
        {
            LlmModelComboBox.IsEnabled = UseLlmCheckBox.IsChecked == true;
        };
    }

    private void SetupDragDrop()
    {
        FileQueueListBox.AddHandler(DragDrop.DropEvent, OnDrop);
        FileQueueListBox.AddHandler(DragDrop.DragOverEvent, OnDragOver);
    }

    private void SetupKeyboardShortcuts()
    {
        FileQueueListBox.KeyDown += (sender, e) =>
        {
            if (e.Key == Key.Delete && FileQueueListBox.SelectedItem is string selectedFile)
            {
                FilesToConvert.Remove(selectedFile);
                e.Handled = true;
            }
        };
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var hasFiles = e.DataTransfer.Formats.Any(f => f == DataFormat.File);
        e.DragEffects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var hasFiles = e.DataTransfer.Formats.Any(f => f == DataFormat.File);
        if (!hasFiles)
        {
            return;
        }

        foreach (var item in e.DataTransfer.Items)
        {
            if (item.Formats.Any(f => f == DataFormat.File))
            {
                var data = item.TryGetRaw(DataFormat.File);
                if (data is IStorageItem storageItem)
                {
                    var path = storageItem.Path.LocalPath;
                    if (!string.IsNullOrEmpty(path) && !FilesToConvert.Contains(path))
                    {
                        FilesToConvert.Add(path);
                    }
                }
                else if (data is IEnumerable<IStorageItem> storageItems)
                {
                    foreach (var si in storageItems)
                    {
                        var path = si.Path.LocalPath;
                        if (!string.IsNullOrEmpty(path) && !FilesToConvert.Contains(path))
                        {
                            FilesToConvert.Add(path);
                        }
                    }
                }
            }
        }
    }

    private void ModelComboBox_SelectionChanged(object? sender, 
        SelectionChangedEventArgs e)
    {
        if (ModelComboBox.SelectedIndex >= 0 && ModelComboBox.SelectedIndex 
            < ModelOptions.Count)
        {
            SelectedModel = ModelOptions[ModelComboBox.SelectedIndex];
        }
    }

    private async void BrowseFiles_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
        {
            Title = "Select Video Files",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Video Files")
                {
                    Patterns = new[] { "*.mp4", "*.mkv", "*.avi", "*.mov", "*.wmv", "*.webm", "*.flv" }
                },
                FilePickerFileTypes.All
            }
        });

        foreach (var file in files)
        {
            var path = file.Path.LocalPath;
            if (!FilesToConvert.Contains(path))
            {
                FilesToConvert.Add(path);
            }
        }
    }

    private async void BrowseFolder_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
        {
            Title = "Select Folder",
            AllowMultiple = false
        });

        if (folders.Count == 0)
        {
            return;
        }

        var folderPath = folders[0].Path.LocalPath;
        var videoExtensions = new[] { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".flv" };

        var files = Directory.EnumerateFiles(folderPath, "*.*", 
            SearchOption.AllDirectories)
            .Where(f => videoExtensions.Contains(Path.GetExtension(f)
            .ToLowerInvariant()));

        foreach (var file in files)
        {
            if (!FilesToConvert.Contains(file))
            {
                FilesToConvert.Add(file);
            }
        }
    }

    private void ClearQueue_Click(object? sender, RoutedEventArgs e)
    {
        FilesToConvert.Clear();
    }

    private async void Engage_Click(object? sender, RoutedEventArgs e)
    {
        if (FilesToConvert.Count == 0)
        {
            StatusText.Text = "No files selected";
            return;
        }

        SetControlsEnabled(false);
        CancelButton.IsVisible = true;
        ResultsTextBox.Text = "";
        Cts = new CancellationTokenSource();
        var cancellationToken = Cts.Token;

        try
        {
            var languageItem = LanguageComboBox.SelectedItem as ComboBoxItem;
            var language = languageItem?.Tag?.ToString() ?? "auto";

            var llmModelItem = LlmModelComboBox.SelectedItem as ComboBoxItem;
            var llmModel = llmModelItem?.Content?.ToString() ?? "phi3:latest";

            var options = new TranscriptionPipelineOptions
            {
                AppPath = AppRootPath,
                Model = SelectedModel,
                Language = language,
                UseVad = UseVadCheckBox.IsChecked == true,
                UseLlmRefinement = UseLlmCheckBox.IsChecked == true,
                LlmModel = llmModel
            };

            // Check if we need to download models first
            if (!DependencyChecker.IsWhisperModelAvailable(AppRootPath, 
                SelectedModel.FileName))
            {
                StatusText.Text = $"Downloading {SelectedModel.DisplayName} model...";
                ProgressBar.IsIndeterminate = false;
                ProgressBar.Maximum = 1;

                var downloadProgress = new Progress<(long Downloaded, long Total)>(p =>
                {
                    var downloadedMb = p.Downloaded / 1024.0 / 1024.0;
                    var totalMb = p.Total / 1024.0 / 1024.0;
                    if (p.Total > 0)
                    {
                        ProgressBar.Value = (double)p.Downloaded / p.Total;
                        StatusText.Text = $"Downloading {SelectedModel.DisplayName}: {downloadedMb:F1} / {totalMb:F0} MB";
                    }
                    else
                    {
                        StatusText.Text = $"Downloading {SelectedModel.DisplayName}: {downloadedMb:F1} MB";
                    }
                });

                var success = await DependencyChecker.DownloadWhisperModelAsync(
                    AppRootPath, SelectedModel.EnumType, SelectedModel.FileName, 
                    SelectedModel.ExpectedSizeBytes, downloadProgress);

                ProgressBar.Value = 0;

                if (!success)
                {
                    StatusText.Text = $"Failed to download {SelectedModel.DisplayName} model";
                    return;
                }
            }

            if (options.UseVad && !DependencyChecker.IsSileroVadModelAvailable(
                AppRootPath))
            {
                StatusText.Text = "Downloading VAD model...";
                ProgressBar.IsIndeterminate = true;

                var success = await DependencyChecker.DownloadSileroVadModelAsync(
                    AppRootPath);

                ProgressBar.IsIndeterminate = false;

                if (!success)
                {
                    StatusText.Text = "Failed to download VAD model";
                    return;
                }
            }

            using var pipeline = new TranscriptionPipeline(options);

            var statusProgress = new Progress<string>(msg =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    StatusText.Text = msg;
                });
            });

            var progressBar = new Progress<double>(p =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    ProgressBar.Value = p;
                });
            });

            ProgressBar.IsIndeterminate = true;
            await Task.Run(async () =>
            {
                await pipeline.InitializeAsync(statusProgress, cancellationToken);
            });
            ProgressBar.IsIndeterminate = false;

            var filesToProcess = FilesToConvert.ToList();
            var totalFiles = filesToProcess.Count;

            for (int i = 0;i < filesToProcess.Count;i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var videoPath = filesToProcess[i];
                var videoDir = Path.GetDirectoryName(videoPath)!;
                var videoName = Path.GetFileNameWithoutExtension(videoPath);
                var srtPath = Path.Combine(videoDir, $"{videoName}.srt");

                var fileStatusProgress = new Progress<string>(msg =>
                {
                    StatusText.Text = $"[{i + 1}/{totalFiles}] {msg}";
                });

                await Task.Run(async () =>
                {
                    await pipeline.ProcessVideoAsync(videoPath, srtPath, fileStatusProgress, progressBar, cancellationToken);
                });

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    FilesToConvert.Remove(videoPath);
                    ResultsTextBox.Text += $"✓ {Path.GetFileName(videoPath)} → {Path.GetFileName(srtPath)}\n";
                });
            }

            StatusText.Text = "Done!";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Cancelled";
            ResultsTextBox.Text += "\n⚠ Operation cancelled by user\n";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
            ResultsTextBox.Text = ex.ToString();
        }
        finally
        {
            SetControlsEnabled(true);
            CancelButton.IsVisible = false;
            ProgressBar.Value = 0;
            ProgressBar.IsIndeterminate = false;
            Cts?.Dispose();
            Cts = null;
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Cts?.Cancel();
        StatusText.Text = "Cancelling...";
    }

    private void SetControlsEnabled(bool enabled)
    {
        EngageButton.IsEnabled = enabled;
        ModelComboBox.IsEnabled = enabled;
        LanguageComboBox.IsEnabled = enabled;
        UseVadCheckBox.IsEnabled = enabled;
        UseLlmCheckBox.IsEnabled = enabled;
        LlmModelComboBox.IsEnabled = enabled && UseLlmCheckBox.IsChecked == true;
    }

    private void About_Click(object? sender, RoutedEventArgs e)
    {
        var aboutWindow = new AboutWindow();
        aboutWindow.ShowDialog(this);
    }
}
