using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace WhatHuh.Core.Services;

public static partial class AudioPreprocessor
{
    [GeneratedRegex(@"Duration:\s*(\d{2}):(\d{2}):(\d{2})\.(\d{2})")]
    private static partial Regex DurationRegex();

    [GeneratedRegex(@"time=(\d{2}):(\d{2}):(\d{2})\.(\d{2})")]
    private static partial Regex TimeRegex();

    public static async Task<string> ExtractAndPreprocessAudioAsync(
        string videoPath,
        string outputPath,
        IProgress<string>? status = null,
        IProgress<double>? progress = null)
    {
        status?.Report($"Extracting audio from: {Path.GetFileName(videoPath)}");
        progress?.Report(0);

        var arguments = BuildFfmpegArguments(videoPath, outputPath);
        
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        var stderr = new StringBuilder();
        double totalDurationSeconds = 0;
        
        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data == null) return;
            stderr.AppendLine(e.Data);
            
            // Parse duration from ffmpeg output
            if (totalDurationSeconds == 0)
            {
                var durationMatch = DurationRegex().Match(e.Data);
                if (durationMatch.Success)
                {
                    totalDurationSeconds = int.Parse(durationMatch.Groups[1].Value) * 3600 +
                                           int.Parse(durationMatch.Groups[2].Value) * 60 +
                                           int.Parse(durationMatch.Groups[3].Value) +
                                           int.Parse(durationMatch.Groups[4].Value) / 100.0;
                }
            }
            
            // Parse current time progress
            if (totalDurationSeconds > 0)
            {
                var timeMatch = TimeRegex().Match(e.Data);
                if (timeMatch.Success)
                {
                    var currentSeconds = int.Parse(timeMatch.Groups[1].Value) * 3600 +
                                         int.Parse(timeMatch.Groups[2].Value) * 60 +
                                         int.Parse(timeMatch.Groups[3].Value) +
                                         int.Parse(timeMatch.Groups[4].Value) / 100.0;
                    progress?.Report(Math.Min(currentSeconds / totalDurationSeconds, 1.0));
                }
            }
        };
        
        process.Start();
        process.BeginErrorReadLine();
        
        // Drain stdout to prevent buffer deadlock
        await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"FFmpeg failed: {stderr}");
        }

        progress?.Report(1.0);
        status?.Report("Audio extraction complete");
        return outputPath;
    }

    public static async Task<MemoryStream> ExtractToMemoryStreamAsync(
        string videoPath,
        IProgress<string>? status = null,
        IProgress<double>? progress = null)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.wav");
        
        try
        {
            await ExtractAndPreprocessAudioAsync(videoPath, tempPath, status, progress);
            
            var memoryStream = new MemoryStream();
            await using (var fileStream = File.OpenRead(tempPath))
            {
                await fileStream.CopyToAsync(memoryStream);
            }
            memoryStream.Position = 0;
            return memoryStream;
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    public static async Task<string> ExtractRawAudioAsync(
        string videoPath,
        string outputPath,
        IProgress<string>? status = null,
        IProgress<double>? progress = null)
    {
        status?.Report($"Extracting raw audio from: {Path.GetFileName(videoPath)}");
        progress?.Report(0);

        var arguments = $"-y -i \"{videoPath}\" -ar 16000 -ac 1 -c:a pcm_s16le \"{outputPath}\"";

        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        var stderr = new StringBuilder();
        double totalDurationSeconds = 0;
        
        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data == null) return;
            stderr.AppendLine(e.Data);
            
            if (totalDurationSeconds == 0)
            {
                var durationMatch = DurationRegex().Match(e.Data);
                if (durationMatch.Success)
                {
                    totalDurationSeconds = int.Parse(durationMatch.Groups[1].Value) * 3600 +
                                           int.Parse(durationMatch.Groups[2].Value) * 60 +
                                           int.Parse(durationMatch.Groups[3].Value) +
                                           int.Parse(durationMatch.Groups[4].Value) / 100.0;
                }
            }
            
            if (totalDurationSeconds > 0)
            {
                var timeMatch = TimeRegex().Match(e.Data);
                if (timeMatch.Success)
                {
                    var currentSeconds = int.Parse(timeMatch.Groups[1].Value) * 3600 +
                                         int.Parse(timeMatch.Groups[2].Value) * 60 +
                                         int.Parse(timeMatch.Groups[3].Value) +
                                         int.Parse(timeMatch.Groups[4].Value) / 100.0;
                    progress?.Report(Math.Min(currentSeconds / totalDurationSeconds, 1.0));
                }
            }
        };
        
        process.Start();
        process.BeginErrorReadLine();
        
        // Drain stdout to prevent buffer deadlock
        await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"FFmpeg failed: {stderr}");
        }

        progress?.Report(1.0);
        return outputPath;
    }

    private static string BuildFfmpegArguments(string inputPath, string outputPath)
    {
        // Advanced filter chain for speech enhancement:
        // - highpass=f=200: Remove sub-vocal rumble and DC offset
        // - lowpass=f=3500: Eliminate ultrasonic noise and aliasing
        // - afftdn=nr=0.21:nf=-25: FFT-based denoising with spectral gating
        // - loudnorm=I=-16:tp=-1.5: EBU R128 loudness normalization
        var filterChain = "highpass=f=200,lowpass=f=3500,afftdn=nr=0.21:nf=-25,loudnorm=I=-16:tp=-1.5";
        
        return $"-y -i \"{inputPath}\" -af \"{filterChain}\" -ar 16000 -ac 1 -c:a pcm_s16le \"{outputPath}\"";
    }
}
