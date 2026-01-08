using System.Diagnostics;
using Whisper.net;
using Whisper.net.Ggml;

namespace WhatHuh.Core.Services;

public enum FfmpegHardwareAccel
{
    None,
    Cuda,
    Qsv,
    Amf,
    VideoToolbox
}

public static class DependencyChecker
{
    private const string SileroVadModelUrl = "https://huggingface.co/deepghs/silero-vad-onnx/resolve/main/silero_vad.onnx";
    private const string SileroVadModelFileName = "silero_vad.onnx";
    
    private static FfmpegHardwareAccel? CachedHwAccel;

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

    public static FfmpegHardwareAccel DetectFfmpegHardwareAccel()
    {
        if (CachedHwAccel.HasValue)
        {
            return CachedHwAccel.Value;
        }

        CachedHwAccel = DetectHwAccelInternal();
        return CachedHwAccel.Value;
    }

    private static FfmpegHardwareAccel DetectHwAccelInternal()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = "-hide_banner -hwaccels",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return FfmpegHardwareAccel.None;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            if (process.ExitCode != 0) return FfmpegHardwareAccel.None;

            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            
            // Check in order of preference: CUDA (NVIDIA), QSV (Intel), AMF (AMD), VideoToolbox (macOS)
            foreach (var line in lines)
            {
                var trimmed = line.Trim().ToLowerInvariant();
                if (trimmed == "cuda" && HasNvdecSupport())
                {
                    return FfmpegHardwareAccel.Cuda;
                }
            }

            foreach (var line in lines)
            {
                var trimmed = line.Trim().ToLowerInvariant();
                if (trimmed == "qsv")
                {
                    return FfmpegHardwareAccel.Qsv;
                }
            }

            foreach (var line in lines)
            {
                var trimmed = line.Trim().ToLowerInvariant();
                if (trimmed == "amf" || trimmed == "d3d11va")
                {
                    return FfmpegHardwareAccel.Amf;
                }
            }

            foreach (var line in lines)
            {
                var trimmed = line.Trim().ToLowerInvariant();
                if (trimmed == "videotoolbox")
                {
                    return FfmpegHardwareAccel.VideoToolbox;
                }
            }

            return FfmpegHardwareAccel.None;
        }
        catch
        {
            return FfmpegHardwareAccel.None;
        }
    }

    private static bool HasNvdecSupport()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = "-hide_banner -decoders",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return false;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            return output.Contains("h264_cuvid") || output.Contains("hevc_cuvid");
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

    public static async Task<bool> DownloadWhisperModelAsync(string appPath, GgmlType modelType, string fileName, IProgress<double>? progress = null)
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
                progress?.Report(totalBytesRead);
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
