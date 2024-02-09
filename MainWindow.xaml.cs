using NAudio.Wave;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using Whisper.net;
using Whisper.net.Ggml;
using Wpf.Ui.Controls;

namespace WhatHuh;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : UiWindow
{
    public ObservableCollection<string> FilesToConvert { get; set; } = [];
    public static List<WhisperModelOption> ModelOptions
    {
        get;
        set;
    } =
    [
        new WhisperModelOption { EnumType = GgmlType.Base, DisplayName = "Base", 
            FileName = "ggml-base.bin" },
        new WhisperModelOption { EnumType = GgmlType.BaseEn, DisplayName = "Base English Only", 
            FileName = "ggml-base-en.bin" },
        new WhisperModelOption { EnumType = GgmlType.Small, DisplayName = "Small", 
            FileName = "ggml-sm.bin" },
        new WhisperModelOption { EnumType = GgmlType.SmallEn, DisplayName = "Small English Only", 
            FileName = "ggml-sm-en.bin" },
        new WhisperModelOption { EnumType = GgmlType.Medium, DisplayName = "Medium", 
            FileName = "ggml-med.bin" },
        new WhisperModelOption { EnumType = GgmlType.MediumEn, DisplayName = "Medium English Only", 
            FileName = "ggml-med-en.bin" },
        new WhisperModelOption { EnumType = GgmlType.LargeV1, DisplayName = "Large v1", 
            FileName = "ggml-lg1.bin" },
        new WhisperModelOption { EnumType = GgmlType.LargeV2, DisplayName = "Large v2", 
            FileName = "ggml-lg2.bin" },
        new WhisperModelOption { EnumType = GgmlType.LargeV3, DisplayName = "Large v3", 
            FileName = "ggml-lgv3.bin" },
    ];
    public static WhisperModelOption SelectedModel { get; set; } = ModelOptions[0];

    public class WhisperModelOption
    {
        public GgmlType EnumType { get; set; }
        public string DisplayName { get; set; }
        public string FileName { get; set; }
    }

    public string AppRootPath { get; set; }

    public MainWindow()
    {
        InitializeComponent();
        this.DataContext = this;
        AppRootPath = AppContext.BaseDirectory;
    }

    private async void EngageClicked(object sender, RoutedEventArgs e)
    {
        if(FilesToConvert.Count == 0)
        {
            RootSnackbar.Show("Whoops", "No files selected", Wpf.Ui.Common.SymbolRegular.Warning16);
            return;
        }
        if(SelectedModel == null)
        {
            RootSnackbar.Show("Whoops", "No model selected", Wpf.Ui.Common.SymbolRegular.Warning16);
            return;
        }
        pbMainProgress.IsIndeterminate = true;
        Progress<string> resultUpdate = new Progress<string>();
        resultUpdate.ProgressChanged += (a, b) =>
        {
            if(txtResults.Text.Count(a => a == '\n') >= 150)
            {
                txtResults.Text = "";
            }
            txtResults.Text += b + "\r\n";
            txtResults.ScrollToEnd();
        };
        Progress<string> statusUpdate = new Progress<string>();
        statusUpdate.ProgressChanged += (a, b) =>
        {
            lblStatus.Content = b;
        };
        Progress<double> updateProgress = new Progress<double>();
        updateProgress.ProgressChanged += (a, b) =>
        {
            pbMainProgress.Value = b;
        };

        btnBrowseFiles.IsEnabled = false;
        btnEngage.IsEnabled = false;

        IProgress<string> status = statusUpdate;
        IProgress<string> results = resultUpdate;
        IProgress<double> progressBar = updateProgress;

        var modelName = SelectedModel.FileName;
        if(!File.Exists(modelName))
        {
            status.Report("Downloading Model....");
            using var modelStream = await WhisperGgmlDownloader.GetGgmlModelAsync(SelectedModel.EnumType);
            using var fileWriter = File.OpenWrite(AppRootPath + "\\" + modelName);
            modelStream.CopyTo(fileWriter);
        }

        var modelPath = AppRootPath + "\\" + modelName;
        status.Report("Loading Model....");
        using var whisperFactory = WhisperFactory.FromPath(modelPath);

        status.Report("Model Loaded!");
        var targetFiles = FilesToConvert.ToList();

        foreach(var file in targetFiles)
        {
            status.Report("Extracting Audio From: " + file);

            var currentFileName = Path.GetFileNameWithoutExtension(file);
            var videoFilePath = Path.GetDirectoryName(file);
            var currentAudioFilePath = videoFilePath + "\\" + currentFileName + ".wav";
            var tempAudioFilePath = videoFilePath + "\\" + "temp.wav";
            var subtitleFileName = videoFilePath + "\\" + currentFileName + ".srt";

            using(var reader = new MediaFoundationReader(file))
            {
                using var writer = new WaveFileWriter(tempAudioFilePath, reader.WaveFormat);
                {
                    byte[] buffer = new byte[reader.WaveFormat.AverageBytesPerSecond * 4];
                    while(true)
                    {
                        int bytesRead = await reader.ReadAsync(buffer, 0, buffer.Length);
                        if(bytesRead == 0)
                        {
                            break;// eos
                        }
                        await writer.WriteAsync(buffer, 0, bytesRead);
                    }
                }
            }
            // Fix format to work with whisper
            using var wavReader = new WaveFileReader(tempAudioFilePath);
            {
                var newFormat = new WaveFormat(16000, 8, 1);
                using var conversionStream = new WaveFormatConversionStream(newFormat, wavReader);
                {
                    WaveFileWriter.CreateWaveFile(currentAudioFilePath, conversionStream);
                }
            }
            status.Report("Audio Extracted! Starting Subs...");
            File.Delete(tempAudioFilePath);

            status.Report("Loading Model...");

            using(var processor = whisperFactory.CreateBuilder().WithLanguage("auto").Build())
            {
                using var fileStream = File.OpenRead(currentAudioFilePath);
                {
                    using TextWriter tw = new StreamWriter(subtitleFileName);
                    {
                        status.Report("Reading Subs...");
                        var sequence = 1;
                        await foreach(var result in processor.ProcessAsync(fileStream))
                        {
                            var srtLine = $"{sequence}\n{FormatTime(result.Start)} --> {FormatTime(result.End)}\n{result.Text}\n\n";
                            tw.Write(srtLine);
                            results.Report($"{sequence} : {FormatTime(result.Start)} --> {FormatTime(result.End)}\n{result.Text}\n");
                            sequence++;
                        }
                    }
                }
            }
            FilesToConvert.Remove(file);
            File.Delete(currentAudioFilePath);
        }

        btnBrowseFiles.IsEnabled = true;
        btnEngage.IsEnabled = true;
        pbMainProgress.IsIndeterminate = false;
        lblStatus.Content = "Done!";
    }

    static string FormatTime(TimeSpan time)
    {
        return time.ToString(@"hh\:mm\:ss\,fff", CultureInfo.InvariantCulture);
    }

    private void ListBox_Drop(object sender, DragEventArgs e)
    {
        if(e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if(files == null || files.Length == 0)
            {
                return;
            }
            foreach(var item in files)
            {
                if(string.IsNullOrEmpty(item) || FilesToConvert.Contains(item))
                {
                    continue;
                }
                FilesToConvert.Add(item);
            }
        }
    }

    private void btnBrowseFiles_Click(object sender, RoutedEventArgs e)
    {
        var fileBrowser = new Microsoft.Win32.OpenFileDialog() { CheckFileExists = true, Multiselect = true, };
        fileBrowser.ShowDialog();
        if(fileBrowser.FileNames != null && fileBrowser.FileNames.Length > 0)
        {
            foreach(string item in fileBrowser.FileNames)
            {
                FilesToConvert.Add(item.Trim());
            }
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Environment.Exit(0);
    }

    private void FolderBrowse_Click(object sender, RoutedEventArgs e)
    {
        var selectedFolder = new System.Windows.Forms.FolderBrowserDialog();
        var userFolderDialog = selectedFolder.ShowDialog();
        if(userFolderDialog == System.Windows.Forms.DialogResult.OK)
        {
            var folderFiles = Directory.EnumerateFiles(selectedFolder.SelectedPath, "*.*", SearchOption.AllDirectories);
            foreach(var file in folderFiles)
            {
                FilesToConvert.Add(file.Trim());
            }
        }
    }

    private async void About_Click(object sender, RoutedEventArgs e)
    {
        About about = new About();
        about.ShowDialog();
    }

    private void Help_Click(object sender, RoutedEventArgs e)
    {
        var myProcess = new System.Diagnostics.Process();
        myProcess.StartInfo.UseShellExecute = true;
        myProcess.StartInfo.FileName = "https://github.com/Echostorm44/WhatHuh/wiki";
        myProcess.Start();
    }

    private void ClearSelected_Click(object sender, RoutedEventArgs e)
    {
        FilesToConvert.Clear();
    }
}