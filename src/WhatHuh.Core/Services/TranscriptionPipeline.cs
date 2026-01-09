using WhatHuh.Core.Models;
using WhatHuh.Core.Services.SileroVad;

namespace WhatHuh.Core.Services;

public class TranscriptionPipeline : IDisposable
{
    private readonly TranscriptionPipelineOptions Options;
    private WhisperTranscriptionService? WhisperService;
    private SileroVadDetector? VadDetector;
    private LlmRefinementService? LlmService;

    public TranscriptionPipeline(TranscriptionPipelineOptions options)
    {
        Options = options;
    }

    public void Dispose()
    {
        WhisperService?.Dispose();
        VadDetector?.Dispose();
        GC.SuppressFinalize(this);
    }

    public async Task InitializeAsync(IProgress<string>? status = null, CancellationToken cancellationToken = default)
    {
        status?.Report("Checking dependencies...");

        if (!DependencyChecker.IsFfmpegAvailable())
        {
            throw new InvalidOperationException(
                "FFmpeg is required but not found. Please install FFmpeg and ensure it's in your PATH.");
        }

        var whisperModelPath = DependencyChecker.GetWhisperModelPath(
            Options.AppPath, Options.Model.FileName);
        if (!File.Exists(whisperModelPath))
        {
            status?.Report($"Downloading Whisper model: {Options.Model.DisplayName}...");
            var success = await DependencyChecker.DownloadWhisperModelAsync(
                Options.AppPath, 
                Options.Model.EnumType, 
                Options.Model.FileName,
                Options.Model.ExpectedSizeBytes);

            if (!success)
            {
                throw new InvalidOperationException(
                    $"Failed to download Whisper model: {Options.Model.DisplayName}");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        status?.Report("Loading Whisper model...");
        WhisperService = new WhisperTranscriptionService(whisperModelPath, 
            Options.Language, Options.BeamSize);
        status?.Report($"Whisper loaded (Runtime: {WhisperTranscriptionService.LoadedRuntime})");

        if (Options.UseVad)
        {
            var vadModelPath = DependencyChecker.GetSileroVadModelPath(
                Options.AppPath);
            if (!File.Exists(vadModelPath))
            {
                status?.Report("Downloading Silero VAD model...");
                var success = await DependencyChecker.DownloadSileroVadModelAsync(
                    Options.AppPath);
                if (!success)
                {
                    throw new InvalidOperationException(
                        "Failed to download Silero VAD model");
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            status?.Report("Loading VAD model...");
            VadDetector = new SileroVadDetector(vadModelPath);
        }

        if (Options.UseLlmRefinement)
        {
            if (!await LlmRefinementService.IsAvailableAsync())
            {
                throw new InvalidOperationException(
                    "Ollama is not running. Please start Ollama to use LLM refinement.");
            }

            // Check if model exists, if not, pull it
            if (!await LlmRefinementService.IsModelAvailableAsync(Options.LlmModel))
            {
                status?.Report($"Pulling LLM model: {Options.LlmModel}...");
                var pullProgress = new Progress<string>(msg => status?.Report($"Pulling {Options.LlmModel}: {msg}"));
                await LlmRefinementService.PullModelAsync(Options.LlmModel, pullProgress, cancellationToken);
            }

            LlmService = new LlmRefinementService(Options.LlmModel);
        }

        status?.Report("Initialization complete");
    }

    public async Task ProcessVideoAsync(
        string videoPath,
        string outputSrtPath,
        IProgress<string>? status = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (WhisperService == null)
        {
            throw new InvalidOperationException(
                "Pipeline not initialized. Call InitializeAsync first.");
        }

        var fileName = Path.GetFileName(videoPath);
        var tempWavPath = Path.Combine(Path.GetTempPath(), 
            $"whathuh_{Guid.NewGuid()}.wav");

        try
        {
            status?.Report($"Extracting audio: {fileName}");
            progress?.Report(0);
            await AudioPreprocessor.ExtractAndPreprocessAudioAsync(videoPath, 
                tempWavPath, null, progress, cancellationToken);

            List<SpeechSegment>? speechSegments = null;
            if (Options.UseVad && VadDetector != null)
            {
                status?.Report("Detecting speech segments...");
                progress?.Report(0);
                speechSegments = VadDetector.GetSpeechSegments(tempWavPath, 
                    progress, cancellationToken);
                status?.Report($"Found {speechSegments.Count} speech segments");
            }

            status?.Report("Transcribing audio...");
            progress?.Report(0);
            var results = new List<TranscriptionResult>();

            if (speechSegments != null && speechSegments.Count > 0)
            {
                await foreach (var result in WhisperService.TranscribeWithVadAsync(
                    tempWavPath, speechSegments, progress, cancellationToken))
                {
                    results.Add(result);
                }
            }
            else
            {
                await using var wavStream = File.OpenRead(tempWavPath);
                await foreach (var result in WhisperService.TranscribeAsync(
                    wavStream, progress, cancellationToken))
                {
                    results.Add(result);
                }
            }

            if (Options.UseLlmRefinement && LlmService != null && 
                results.Count > 0)
            {
                status?.Report("Refining with LLM...");
                progress?.Report(0);
                results = await LlmService.RefineBatchAsync(results, 25, progress, cancellationToken);
            }

            status?.Report("Writing subtitle file...");
            progress?.Report(0);
            await Utilities.SrtWriter.WriteAsync(outputSrtPath, results);
            progress?.Report(1.0);

            status?.Report($"Completed: {fileName}");
        }
        finally
        {
            if (File.Exists(tempWavPath))
            {
                File.Delete(tempWavPath);
            }
        }
    }

    public async Task ProcessBatchAsync(
        IEnumerable<string> videoPaths,
        IProgress<string>? status = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var videos = videoPaths.ToList();

        for (int i = 0;i < videos.Count;i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var videoPath = videos[i];
            var videoDir = Path.GetDirectoryName(videoPath)!;
            var videoName = Path.GetFileNameWithoutExtension(videoPath);
            var srtPath = Path.Combine(videoDir, $"{videoName}.srt");

            var batchStatus = new Progress<string>(msg =>
            {
                status?.Report($"[{i + 1}/{videos.Count}] {msg}");
            });

            await ProcessVideoAsync(videoPath, srtPath, batchStatus, progress, cancellationToken);
        }
    }
}
