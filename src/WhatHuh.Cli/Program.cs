using Spectre.Console;
using System.CommandLine;
using System.CommandLine.Invocation;
using WhatHuh.Core.Models;
using WhatHuh.Core.Services;

namespace WhatHuh.Cli;

public class Program
{
    private static CancellationTokenSource? Cts;

    public static async Task<int> Main(string[] args)
    {
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            Cts?.Cancel();
            AnsiConsole.MarkupLine("\n[yellow]Cancellation requested, cleaning up...[/]");
        };

        var inputArg = new Argument<string>("input") 
        { 
            Description = "Input video file, directory, or glob pattern (e.g., *.mp4)"
        };
        
        var outputOption = new Option<string?>("--output") 
        { 
            Description = "Output SRT file path (default: same as input with .srt extension)"
        };
        outputOption.Aliases.Add("-o");
        
        var modelOption = new Option<string>("--model") 
        { 
            Description = "Whisper model to use for transcription",
            DefaultValueFactory = _ => "base" 
        };
        modelOption.Aliases.Add("-m");
        
        var languageOption = new Option<string>("--language") 
        { 
            Description = "Language code or 'auto' for detection",
            DefaultValueFactory = _ => "auto" 
        };
        languageOption.Aliases.Add("-l");
        
        var noVadOption = new Option<bool>("--no-vad") 
        { 
            Description = "Disable Voice Activity Detection"
        };
        noVadOption.Aliases.Add("-n");
        
        var refineOption = new Option<bool>("--refine") 
        { 
            Description = "Enable LLM refinement using Ollama"
        };
        refineOption.Aliases.Add("-r");
        
        var llmModelOption = new Option<string>("--llm-model") 
        { 
            Description = "LLM model for refinement",
            DefaultValueFactory = _ => "phi3:mini" 
        };
        
        var beamSizeOption = new Option<int>("--beam-size") 
        { 
            Description = "Beam size for decoding (higher = better quality, slower)",
            DefaultValueFactory = _ => 5 
        };
        beamSizeOption.Aliases.Add("-b");

        var transcribeCommand = new Command("transcribe", 
            "Transcribe video files to SRT subtitles");
        transcribeCommand.Arguments.Add(inputArg);
        transcribeCommand.Options.Add(outputOption);
        transcribeCommand.Options.Add(modelOption);
        transcribeCommand.Options.Add(languageOption);
        transcribeCommand.Options.Add(noVadOption);
        transcribeCommand.Options.Add(refineOption);
        transcribeCommand.Options.Add(llmModelOption);
        transcribeCommand.Options.Add(beamSizeOption);
        transcribeCommand.Aliases.Add("-t");

        transcribeCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var input = parseResult.GetValue(inputArg)!;
            var output = parseResult.GetValue(outputOption);
            var model = parseResult.GetValue(modelOption)!;
            var language = parseResult.GetValue(languageOption)!;
            var noVad = parseResult.GetValue(noVadOption);
            var refine = parseResult.GetValue(refineOption);
            var llmModel = parseResult.GetValue(llmModelOption)!;
            var beamSize = parseResult.GetValue(beamSizeOption);
            
            await RunTranscribeAsync(input, output, model, language, noVad, refine, 
                llmModel, beamSize);
        });

        var ffmpegOption = new Option<bool>("--ffmpeg") 
        { 
            Description = "Check if FFmpeg is available"
        };
        ffmpegOption.Aliases.Add("-f");
        
        var vadOption = new Option<bool>("--vad") 
        { 
            Description = "Download Silero VAD model"
        };
        vadOption.Aliases.Add("-v");
        
        var whisperOption = new Option<string?>("--whisper") 
        { 
            Description = "Download specified Whisper model"
        };
        whisperOption.Aliases.Add("-w");
        
        var ollamaOption = new Option<bool>("--ollama") 
        { 
            Description = "Check Ollama availability"
        };
        
        var allOption = new Option<bool>("--all") 
        { 
            Description = "Setup all dependencies"
        };
        allOption.Aliases.Add("-a");

        var setupCommand = new Command("setup", "Setup and download required dependencies");
        setupCommand.Options.Add(ffmpegOption);
        setupCommand.Options.Add(vadOption);
        setupCommand.Options.Add(whisperOption);
        setupCommand.Options.Add(ollamaOption);
        setupCommand.Options.Add(allOption);
        setupCommand.Aliases.Add("-s");

        setupCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var ffmpeg = parseResult.GetValue(ffmpegOption);
            var vad = parseResult.GetValue(vadOption);
            var whisper = parseResult.GetValue(whisperOption);
            var ollama = parseResult.GetValue(ollamaOption);
            var all = parseResult.GetValue(allOption);
            
            await RunSetupAsync(ffmpeg, vad, whisper, ollama, all);
        });

        var infoCommand = new Command("info", "Display system and configuration information");
        infoCommand.Aliases.Add("-i");
        infoCommand.SetAction(async (parseResult, cancellationToken) => await RunInfoAsync());

        var rootCommand = new RootCommand(GetFullDescription());
        rootCommand.Subcommands.Add(transcribeCommand);
        rootCommand.Subcommands.Add(setupCommand);
        rootCommand.Subcommands.Add(infoCommand);

        return await rootCommand.Parse(args).InvokeAsync();
    }

    private static string GetFullDescription()
{
    return """
        WhatHuh - Video Transcription Tool
        
        A powerful CLI tool for transcribing video files to SRT subtitles using OpenAI's Whisper.
        
        FEATURES:
          • Automatic speech recognition with multiple Whisper model sizes
          • Voice Activity Detection (VAD) for improved accuracy
          • Optional LLM refinement via Ollama for better transcription quality
          • Batch processing of multiple files or entire directories
          • Support for MP4, MKV, AVI, MOV, WMV, WEBM, FLV formats
        
        ══════════════════════════════════════════════════════════════════════════════
        TRANSCRIBE COMMAND (-t, transcribe)
        ══════════════════════════════════════════════════════════════════════════════
        Transcribe video files to SRT subtitles.
        
        Usage: whathuh -t <input> [options]
        
        Arguments:
          <input>                 Video file, directory, or glob pattern (*.mp4)
        
        Options:
          -o, --output <path>     Output SRT file path (default: same as input)
          -m, --model <model>     Whisper model (default: base)
          -l, --language <lang>   Language code or 'auto' (default: auto)
          -n, --no-vad            Disable Voice Activity Detection
          -r, --refine            Enable LLM refinement using Ollama
          --llm-model <model>     LLM model for refinement (default: phi3:mini)
          -b, --beam-size <n>     Beam size for decoding (default: 5)
        
        WHISPER MODELS:
          base        ~142 MB   Fast, good for clear audio
          base-en     ~142 MB   English-only base model
          small       ~466 MB   Better accuracy, moderate speed
          small-en    ~466 MB   English-only small model
          medium      ~1.5 GB   High accuracy, slower
          medium-en   ~1.5 GB   English-only medium model
          large-v1    ~2.9 GB   Best accuracy (original)
          large-v2    ~2.9 GB   Best accuracy (improved)
          large-v3    ~2.9 GB   Best accuracy (latest)
        
        ══════════════════════════════════════════════════════════════════════════════
        SETUP COMMAND (-s, setup)
        ══════════════════════════════════════════════════════════════════════════════
        Setup and download required dependencies.
        
        Usage: whathuh -s [options]
        
        Options:
          -a, --all               Setup all dependencies
          -f, --ffmpeg            Check FFmpeg availability
          -v, --vad               Download Silero VAD model
          -w, --whisper <model>   Download specified Whisper model
          --ollama                Check Ollama availability
        
        ══════════════════════════════════════════════════════════════════════════════
        INFO COMMAND (-i, info)
        ══════════════════════════════════════════════════════════════════════════════
        Display system and configuration information.
        
        Usage: whathuh -i
        
        ══════════════════════════════════════════════════════════════════════════════
        EXAMPLES
        ══════════════════════════════════════════════════════════════════════════════
          whathuh -t video.mp4                    Transcribe single file
          whathuh -t video.mp4 -m large-v3        Use large-v3 model
          whathuh -t video.mp4 -l en -n           English, no VAD
          whathuh -t "C:\Videos" -m medium        Transcribe entire folder
          whathuh -t *.mkv -r                     Transcribe MKV files with LLM refinement
          whathuh -s -a                           Setup all dependencies
          whathuh -s -w large-v3                  Download large-v3 model
          whathuh -i                              Show system info
        """;
    }

    private static async Task RunTranscribeAsync(string input, string? output, string model, 
        string language, bool noVad, bool refine, string llmModel, int beamSize)
    {
        var files = GetInputFiles(input);

    if (files.Count == 0)
    {
        AnsiConsole.MarkupLine("[red]No matching files found.[/]");
        return;
    }

    var modelOpt = GetModelOption(model);
    if (modelOpt == null)
    {
        AnsiConsole.MarkupLine($"[red]Unknown model: {model}[/]");
        ShowAvailableModels();
        return;
    }

    var options = new TranscriptionPipelineOptions
    {
        AppPath = AppContext.BaseDirectory,
        Model = modelOpt,
        Language = language,
        UseVad = !noVad,
        UseLlmRefinement = refine,
        LlmModel = llmModel,
        BeamSize = beamSize
    };

    AnsiConsole.Write(new Rule("[cyan]WhatHuh Transcriber[/]").RuleStyle("grey"));
    AnsiConsole.WriteLine();

    var settingsTable = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey);
    settingsTable.AddColumn("Setting");
    settingsTable.AddColumn("Value");
    settingsTable.AddRow("Model", modelOpt.DisplayName);
    settingsTable.AddRow("Language", language);
    settingsTable.AddRow("VAD", noVad ? "[yellow]Disabled[/]" 
        : "[green]Enabled[/]");
    settingsTable.AddRow("Beam Size", beamSize.ToString());
    settingsTable.AddRow("LLM Refinement", refine ? 
        $"[green]Enabled[/] ({llmModel})" : "[grey]Disabled[/]");
    settingsTable.AddRow("Files", files.Count.ToString());
    AnsiConsole.Write(settingsTable);
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[grey]Press Ctrl+C to cancel[/]");
    AnsiConsole.WriteLine();

    Cts = new CancellationTokenSource();
    var cancellationToken = Cts.Token;

    try
    {
        // Check if we need to download the Whisper model first
        var whisperModelPath = DependencyChecker.GetWhisperModelPath(
            options.AppPath, options.Model.FileName);
        
        if (!File.Exists(whisperModelPath))
        {
            await AnsiConsole.Progress()
                .AutoClear(false)
                .Columns(
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new SpinnerColumn())
                .StartAsync(async ctx =>
                {
                    var task = ctx.AddTask($"[cyan]Downloading {options.Model.DisplayName}[/]");
                    var expectedBytes = options.Model.ExpectedSizeBytes;
                    task.MaxValue = expectedBytes > 0 ? expectedBytes : 100;
                    task.IsIndeterminate = expectedBytes <= 0;
                    
                    var progress = new Progress<(long Downloaded, long Total)>(p =>
                    {
                        task.IsIndeterminate = false;
                        task.Value = p.Downloaded;
                        if (p.Total > 0)
                        {
                            task.MaxValue = p.Total;
                        }
                    });
                    
                    var success = await DependencyChecker.DownloadWhisperModelAsync(
                        options.AppPath, 
                        options.Model.EnumType, 
                        options.Model.FileName,
                        options.Model.ExpectedSizeBytes, 
                        progress);
                    
                    if (!success)
                    {
                        throw new InvalidOperationException(
                            $"Failed to download Whisper model: {options.Model.DisplayName}");
                    }
                    
                    task.Value = task.MaxValue;
                });
            
            AnsiConsole.MarkupLine($"[green]✓[/] Downloaded {options.Model.DisplayName}");
        }

        using var pipeline = new TranscriptionPipeline(options);

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("cyan"))
            .StartAsync("Initializing pipeline...", async ctx =>
            {
                var status = new Progress<string>(msg => ctx.Status(msg));
                await pipeline.InitializeAsync(status, cancellationToken);
            });

        AnsiConsole.MarkupLine("[green]✓[/] Pipeline initialized");
        AnsiConsole.MarkupLine($"[grey]  Runtime: {WhatHuh.Core.Services.WhisperTranscriptionService.LoadedRuntime}[/]");
        AnsiConsole.WriteLine();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var outputPath = output ?? Path.ChangeExtension(file, ".srt");
            var fileName = Path.GetFileName(file);
            var shortName = fileName.Length > 50 ? $"{fileName[..47]}..." 
                : fileName;

            AnsiConsole.MarkupLine($"[bold]Processing:[/] " +
                $"{shortName.EscapeMarkup()}");

            await AnsiConsole.Progress()
                .AutoClear(false)
                .HideCompleted(false)
                .Columns(
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new SpinnerColumn())
                .StartAsync(async ctx =>
                {
                    var task = ctx.AddTask("[grey]Starting...[/]", maxValue: 1);

                    var progress = new Progress<double>(p =>
                    {
                        task.Value = p;
                    });

                    var statusProgress = new Progress<string>(msg =>
                    {
                        task.Description = $"[cyan]{msg.EscapeMarkup()}[/]";
                    });

                    await pipeline.ProcessVideoAsync(file, outputPath, 
                        statusProgress, progress, cancellationToken);
                    task.Value = 1;
                    task.Description = "[green]Complete[/]";
                });

            AnsiConsole.MarkupLine($"[green]✓[/] Created: [link]" +
                $"{outputPath.EscapeMarkup()}[/]");
            AnsiConsole.WriteLine();
        }

        AnsiConsole.Write(new Rule("[green]All files processed successfully![/]").RuleStyle("green"));
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule("[yellow]Operation cancelled[/]").RuleStyle("yellow"));
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
        }
    }

    private static async Task RunSetupAsync(bool ffmpeg, bool vad, string? whisper, 
        bool ollama, bool all)
    {
    AnsiConsole.Write(new Rule("[cyan]WhatHuh Setup[/]").RuleStyle("grey"));
    AnsiConsole.WriteLine();

    var appPath = AppContext.BaseDirectory;

    if (all || ffmpeg)
    {
        var available = DependencyChecker.IsFfmpegAvailable();
        AnsiConsole.MarkupLine(available 
            ? "[green]✓[/] FFmpeg is available" 
            : "[red]✗[/] FFmpeg not found - Install from https://ffmpeg.org/download.html");
    }

    if (all || vad)
    {
        if (DependencyChecker.IsSileroVadModelAvailable(appPath))
        {
            AnsiConsole.MarkupLine("[green]✓[/] Silero VAD model already downloaded");
        }
        else
        {
            await AnsiConsole.Progress()
                .AutoClear(false)
                .Columns(
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new TransferSpeedColumn(),
                    new SpinnerColumn())
                .StartAsync(async ctx =>
                {
                    var task = ctx.AddTask("[cyan]Downloading Silero VAD model[/]");
                    var progress = new Progress<double>(p => task.Value = p * 100);
                    var success = await DependencyChecker
                        .DownloadSileroVadModelAsync(appPath, progress);
                    task.Value = 100;
                    if (!success)
                    {
                        AnsiConsole.MarkupLine("[red]✗[/] Failed to download VAD model");
                    }
                });
            if (DependencyChecker.IsSileroVadModelAvailable(appPath))
            {
                AnsiConsole.MarkupLine("[green]✓[/] Silero VAD model downloaded");
            }
        }
    }

    if (all || !string.IsNullOrEmpty(whisper))
    {
        var modelName = whisper ?? "base";
        var modelOpt = GetModelOption(modelName);
        if (modelOpt == null)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Unknown model: {modelName}");
            ShowAvailableModels();
        }
        else
        {
            if (DependencyChecker.IsWhisperModelAvailable(appPath, 
                modelOpt.FileName))
            {
                AnsiConsole.MarkupLine($"[green]✓[/] Whisper " +
                    $"{modelOpt.DisplayName} already downloaded");
            }
            else
            {
                await AnsiConsole.Progress()
                    .AutoClear(false)
                    .Columns(
                        new TaskDescriptionColumn(),
                        new ProgressBarColumn(),
                        new PercentageColumn(),
                        new SpinnerColumn())
                    .StartAsync(async ctx =>
                    {
                        var task = ctx.AddTask($"[cyan]Downloading " +
                            $"{modelOpt.DisplayName}[/]");
                        var expectedBytes = modelOpt.ExpectedSizeBytes;
                        task.MaxValue = expectedBytes > 0 ? expectedBytes : 100;
                        task.IsIndeterminate = expectedBytes <= 0;
                        
                        var progress = new Progress<(long Downloaded, long Total)>(p =>
                        {
                            task.IsIndeterminate = false;
                            task.Value = p.Downloaded;
                            if (p.Total > 0)
                            {
                                task.MaxValue = p.Total;
                            }
                        });
                        var success = await DependencyChecker
                            .DownloadWhisperModelAsync(appPath, 
                                modelOpt.EnumType, modelOpt.FileName, 
                                modelOpt.ExpectedSizeBytes, progress);
                        if (!success)
                        {
                            AnsiConsole.MarkupLine($"[red]✗[/] Failed to download {modelOpt.DisplayName}");
                        }
                    });
                if (DependencyChecker.IsWhisperModelAvailable(appPath, 
                        modelOpt.FileName))
                {
                    AnsiConsole.MarkupLine($"[green]✓[/] Whisper " +
                        $"{modelOpt.DisplayName} downloaded");
                }
            }
        }
    }

    if (all || ollama)
    {
        var available = await DependencyChecker.IsOllamaAvailableAsync();
        if (available)
        {
            AnsiConsole.MarkupLine("[green]✓[/] Ollama is running");
            var models = await DependencyChecker.GetOllamaModelsAsync();
            if (models.Count > 0)
            {
                AnsiConsole.MarkupLine($"  Models: {string.Join(", ", models)}");
            }
            else
            {
                AnsiConsole.MarkupLine("  [yellow]No models installed. Run 'ollama pull phi3:mini'[/]");
            }
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]○[/] Ollama not running (optional) - Download from https://ollama.ai");
        }
    }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[green]Setup complete![/]");
    }

    private static async Task RunInfoAsync()
    {
        AnsiConsole.Write(new Rule("[cyan]WhatHuh System Info[/]").RuleStyle("grey"));
        AnsiConsole.WriteLine();

        var appPath = AppContext.BaseDirectory;

        var systemTable = new Table().Border(TableBorder.Rounded)
            .BorderColor(Color.Grey);
        systemTable.AddColumn("[bold]System[/]");
        systemTable.AddColumn("[bold]Value[/]");
        systemTable.AddRow("Application Path", appPath);
        systemTable.AddRow(".NET Version", Environment.Version.ToString());
        systemTable.AddRow("OS", Environment.OSVersion.ToString());
        systemTable.AddRow("64-bit Process", Environment.Is64BitProcess 
            ? "[green]Yes[/]" : "No");
        AnsiConsole.Write(systemTable);
        AnsiConsole.WriteLine();

        var depsTable = new Table().Border(TableBorder.Rounded)
            .BorderColor(Color.Grey);
        depsTable.AddColumn("[bold]Dependency[/]");
        depsTable.AddColumn("[bold]Status[/]");
        depsTable.AddRow("FFmpeg", DependencyChecker.IsFfmpegAvailable() 
            ? "[green]Available[/]" : "[red]Not Found[/]");
        depsTable.AddRow("Silero VAD", DependencyChecker
            .IsSileroVadModelAvailable(appPath) ? "[green]Downloaded[/]" 
            : "[yellow]Not Downloaded[/]");
        var ollamaAvailable = await DependencyChecker.IsOllamaAvailableAsync();
        depsTable.AddRow("Ollama", ollamaAvailable ? "[green]Running[/]" 
            : "[yellow]Not Running[/]");
        AnsiConsole.Write(depsTable);
        AnsiConsole.WriteLine();

        var modelsTable = new Table().Border(TableBorder.Rounded)
            .BorderColor(Color.Grey);
        modelsTable.AddColumn("[bold]Whisper Model[/]");
        modelsTable.AddColumn("[bold]Status[/]");
        foreach (var model in WhisperModelOption.GetDefaultOptions())
        {
            var status = DependencyChecker.IsWhisperModelAvailable(appPath, 
                model.FileName)
                ? "[green]Downloaded[/]"
                : "[grey]Not Downloaded[/]";
            modelsTable.AddRow(model.DisplayName, status);
        }
        AnsiConsole.Write(modelsTable);

        if (ollamaAvailable)
        {
            AnsiConsole.WriteLine();
            var llmModels = await DependencyChecker.GetOllamaModelsAsync();
            if (llmModels.Count > 0)
            {
                var llmTable = new Table().Border(TableBorder.Rounded)
                    .BorderColor(Color.Grey);
                llmTable.AddColumn("[bold]Ollama Models[/]");
                foreach (var model in llmModels)
                {
                    llmTable.AddRow(model);
                }
                AnsiConsole.Write(llmTable);
            }
        }
    }

    private static void ShowAvailableModels()
    {
        AnsiConsole.WriteLine();
        var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey);
        table.AddColumn("[bold]Model[/]");
        table.AddColumn("[bold]Size[/]");
        table.AddColumn("[bold]Description[/]");
        table.AddRow("base", "~142 MB", "Fast, good for clear audio");
        table.AddRow("base-en", "~142 MB", "English-only base model");
        table.AddRow("small", "~466 MB", "Better accuracy, moderate speed");
        table.AddRow("small-en", "~466 MB", "English-only small model");
        table.AddRow("medium", "~1.5 GB", "High accuracy, slower");
        table.AddRow("medium-en", "~1.5 GB", "English-only medium model");
        table.AddRow("large-v1", "~2.9 GB", "Best accuracy (original)");
        table.AddRow("large-v2", "~2.9 GB", "Best accuracy (improved)");
        table.AddRow("large-v3", "~2.9 GB", "Best accuracy (latest)");
        AnsiConsole.Write(table);
    }

    private static List<string> GetInputFiles(string input)
    {
        var files = new List<string>();

        if (input.Contains('*') || input.Contains('?'))
        {
            var directory = Path.GetDirectoryName(input);
            if (string.IsNullOrEmpty(directory))
            {
                directory = Directory.GetCurrentDirectory();
            }

            var pattern = Path.GetFileName(input);
            files.AddRange(Directory.GetFiles(directory, pattern, 
                SearchOption.TopDirectoryOnly));
        }
        else if (File.Exists(input))
        {
            files.Add(Path.GetFullPath(input));
        }
        else if (Directory.Exists(input))
        {
            var videoExtensions = new[] { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".flv" };
            files.AddRange(Directory.GetFiles(input, "*.*", SearchOption
                .AllDirectories)
                .Where(f => videoExtensions.Contains(Path.GetExtension(f)
                .ToLowerInvariant())));
        }

        return files;
    }

    private static WhisperModelOption? GetModelOption(string modelName)
    {
        var options = WhisperModelOption.GetDefaultOptions();
        var normalizedName = modelName.ToLowerInvariant().Replace("-", "")
            .Replace("_", "");

        return options.FirstOrDefault(o =>
            o.DisplayName.ToLowerInvariant().Replace(" ", "").Replace("-", "") 
                == normalizedName ||
            o.FileName.ToLowerInvariant().Contains(normalizedName));
    }
}
