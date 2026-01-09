using System.Diagnostics;
using Whisper.net;
using Whisper.net.Ggml;

namespace WhatHuh.Core.Services;

public static class DependencyChecker
{
    private const string SileroVadModelUrl = "https://huggingface.co/deepghs/silero-vad-onnx/resolve/main/silero_vad.onnx";
    private const string SileroVadModelFileName = "silero_vad.onnx";

    public static bool IsFfmpegAvailable()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = "-version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            process?.WaitForExit(5000);
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsSileroVadModelAvailable(string appPath)
    {
        var modelPath = Path.Combine(appPath, SileroVadModelFileName);
        return File.Exists(modelPath);
    }

    public static string GetSileroVadModelPath(string appPath) => Path.Combine(appPath, SileroVadModelFileName);

    public static bool IsWhisperModelAvailable(string appPath, string modelFileName)
    {
        var modelPath = Path.Combine(appPath, modelFileName);
        return File.Exists(modelPath);
    }

    public static string GetWhisperModelPath(string appPath, string modelFileName) => Path.Combine(appPath, modelFileName);

    public static async Task<bool> IsOllamaAvailableAsync()
    {
        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var response = await httpClient.GetAsync("http://localhost:11434/api/tags");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> DownloadSileroVadModelAsync(string appPath, IProgress<double>? progress = null)
    {
        var modelPath = Path.Combine(appPath, SileroVadModelFileName);

        try
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10
            };
            
            using var httpClient = new HttpClient(handler);
            httpClient.DefaultRequestHeaders.Add("User-Agent", "WhatHuh/2.0");
            
            using var response = await httpClient.GetAsync(SileroVadModelUrl, 
                HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            var canReportProgress = totalBytes > 0 && progress != null;

            await using var contentStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(modelPath, FileMode.Create, 
                FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            long totalBytesRead = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                totalBytesRead += bytesRead;

                if (canReportProgress)
                {
                    progress!.Report((double)totalBytesRead / totalBytes);
                }
            }

            // Verify the downloaded file is valid (should be > 1MB for VAD model)
            if (totalBytesRead < 1_000_000)
            {
                File.Delete(modelPath);
                return false;
            }

            return true;
        }
        catch
        {
            if (File.Exists(modelPath))
            {
                File.Delete(modelPath);
            }
            return false;
        }
    }

    public static async Task<bool> DownloadWhisperModelAsync(string appPath, GgmlType modelType, string fileName, long expectedSize = 0, IProgress<(long Downloaded, long Total)>? progress = null)
    {
        var modelPath = Path.Combine(appPath, fileName);

        try
        {
            using var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(modelType);
            await using var fileStream = new FileStream(modelPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[81920];
            long totalBytesRead = 0;
            int bytesRead;

            while ((bytesRead = await modelStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                totalBytesRead += bytesRead;
                progress?.Report((totalBytesRead, expectedSize));
            }

            return true;
        }
        catch
        {
            if (File.Exists(modelPath))
            {
                File.Delete(modelPath);
            }
            return false;
        }
    }

    public static async Task<List<string>> GetOllamaModelsAsync()
    {
        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var response = await httpClient.GetStringAsync("http://localhost:11434/api/tags");

            // Simple JSON parsing without external dependency
            var models = new List<string>();
            var nameStartIndex = 0;
            while ((nameStartIndex = response.IndexOf("\"name\":\"", nameStartIndex, StringComparison.Ordinal)) != -1)
            {
                nameStartIndex += 8;
                var nameEndIndex = response.IndexOf("\"", nameStartIndex, StringComparison.Ordinal);
                if (nameEndIndex != -1)
                {
                    models.Add(response.Substring(nameStartIndex, nameEndIndex - nameStartIndex));
                    nameStartIndex = nameEndIndex;
                }
            }
            return models;
        }
        catch
        {
            return [];
        }
    }
}
