using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using WhatHuh.Core.Models;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace WhatHuh.Core.Services;

public class WhisperTranscriptionService : IDisposable
{
    private readonly WhisperFactory Factory;
    private readonly string Language;
    private readonly int BeamSize;
    public static string LoadedRuntime { get; private set; } = "Unknown";

    static WhisperTranscriptionService()
    {
        RuntimeOptions.RuntimeLibraryOrder = 
        [
            RuntimeLibrary.Cuda,
            RuntimeLibrary.Vulkan,
            RuntimeLibrary.CoreML,
            RuntimeLibrary.OpenVino,
            RuntimeLibrary.Cpu,
            RuntimeLibrary.CpuNoAvx
        ];
    }

    public WhisperTranscriptionService(string modelPath, string language = "auto", int beamSize = 5)
    {
        Factory = WhisperFactory.FromPath(modelPath);
        LoadedRuntime = RuntimeOptions.LoadedLibrary?.ToString() ?? "Unknown";
        Language = language;
        BeamSize = beamSize;
    }

    public void Dispose()
    {
        Factory.Dispose();
        GC.SuppressFinalize(this);
    }

    public async IAsyncEnumerable<TranscriptionResult> TranscribeAsync(
        Stream audioStream,
        IProgress<double>? progress = null)
    {
        var builder = Factory.CreateBuilder()
            .WithLanguage(Language);

        using var processor = builder.Build();

        var sequence = 1;
        var totalLength = audioStream.Length;

        await foreach (var result in processor.ProcessAsync(audioStream))
        {
            yield return new TranscriptionResult
            {
                Sequence = sequence++,
                Start = result.Start,
                End = result.End,
                Text = result.Text
            };

            if (totalLength > 0)
            {
                progress?.Report((double)audioStream.Position / totalLength);
            }
        }
    }

    public async IAsyncEnumerable<TranscriptionResult> TranscribeWithVadAsync(
        string audioFilePath,
        List<SpeechSegment> speechSegments,
        IProgress<double>? progress = null)
    {
        var builder = Factory.CreateBuilder()
            .WithLanguage(Language);

        using var processor = builder.Build();

        var sequence = 1;
        var totalSegments = speechSegments.Count;
        var processedSegments = 0;

        foreach (var segment in speechSegments)
        {
            if (segment.StartSeconds == null || segment.EndSeconds == null)
            {
                continue;
            }

            var segmentStream = await ExtractSegmentAsync(audioFilePath, 
                segment.StartSeconds.Value, 
                segment.EndSeconds.Value);

            await foreach (var result in processor.ProcessAsync(segmentStream))
            {
                var offsetStart = TimeSpan.FromSeconds(segment.StartSeconds.Value);
                yield return new TranscriptionResult
                {
                    Sequence = sequence++,
                    Start = result.Start + offsetStart,
                    End = result.End + offsetStart,
                    Text = result.Text
                };
            }

            processedSegments++;
            progress?.Report((double)processedSegments / totalSegments);
        }
    }

    private static async Task<MemoryStream> ExtractSegmentAsync(
        string audioFilePath, float startSeconds, float endSeconds)
    {
        var memoryStream = new MemoryStream();

        await using var fileStream = File.OpenRead(audioFilePath);
        using var reader = new WaveFileReader(fileStream);

        var sampleRate = reader.WaveFormat.SampleRate;
        var blockAlign = reader.WaveFormat.BlockAlign;

        var startPosition = (long)(startSeconds * sampleRate) * blockAlign;
        var endPosition = (long)(endSeconds * sampleRate) * blockAlign;

        // Align to block boundaries
        startPosition = (startPosition / blockAlign) * blockAlign;
        endPosition = (endPosition / blockAlign) * blockAlign;

        reader.Position = startPosition;
        var bytesToRead = (int)(endPosition - startPosition);

        // Ensure we read complete blocks
        bytesToRead = (bytesToRead / blockAlign) * blockAlign;
        if (bytesToRead <= 0)
        {
            bytesToRead = blockAlign;
        }

        var buffer = new byte[bytesToRead];
        var bytesRead = reader.Read(buffer, 0, bytesToRead);

        var tempProvider = new RawSourceWaveStream(new MemoryStream(buffer, 0, bytesRead), reader.WaveFormat);
        var resampler = new WdlResamplingSampleProvider(tempProvider.ToSampleProvider(), 16000);

        WaveFileWriter.WriteWavFileToStream(memoryStream, resampler.ToWaveProvider16());
        memoryStream.Position = 0;

        return memoryStream;
    }

    public static async Task<MemoryStream> ResampleToWhisperFormatAsync(Stream inputStream)
    {
        var wavStream = new MemoryStream();

        await Task.Run(() =>
        {
            using var reader = new WaveFileReader(inputStream);
            var resampler = new WdlResamplingSampleProvider(reader.ToSampleProvider(), 16000);
            WaveFileWriter.WriteWavFileToStream(wavStream, resampler.ToWaveProvider16());
        });

        wavStream.Position = 0;
        return wavStream;
    }
}
