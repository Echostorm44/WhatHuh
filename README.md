# WhatHuh

Automatically Create Subtitles For Video Files

So if you have older movies or tv shows that are impossible to get subtitles for or you want to easily sub movies you create this is about as easy as it can be.

WhatHuh comes from the frustration of there not being an easy way to get or create subs so that I can still enjoy watching things despite my hearing loss.

## Features

- **Cross-platform** - AvaloniaUI GUI works on Windows, macOS, and Linux
- **CLI Support** - Spectre.Console-powered command line interface for scripting
- **Voice Activity Detection (VAD)** - Silero VAD filters out silence to reduce hallucinations
- **Multiple Whisper Backends** - Automatically uses CUDA, Vulkan, CoreML, OpenVINO, or CPU
- **LLM Refinement** - Optional Ollama integration to clean up transcripts
- **Advanced Audio Preprocessing** - FFmpeg filters for noise reduction and normalization
- **Native AOT** - Self-contained executables with no runtime dependency

## Requirements

- **FFmpeg** - Must be installed and available in PATH
- **Ollama** (optional) - For LLM transcript refinement

## CLI Usage

### Commands

| Command | Alias | Description |
|---------|-------|-------------|
| `transcribe <input>` | `-t` | Transcribe video files to SRT subtitles |
| `setup` | `-s` | Download and configure dependencies |
| `info` | `-i` | Display system and configuration info |

### Transcribe Options

| Option | Alias | Default | Description |
|--------|-------|---------|-------------|
| `--output` | `-o` | (input).srt | Output SRT file path |
| `--model` | `-m` | base | Whisper model size |
| `--language` | `-l` | auto | Language code or 'auto' for detection |
| `--no-vad` | `-n` | false | Disable Voice Activity Detection |
| `--refine` | `-r` | false | Enable LLM refinement via Ollama |
| `--llm-model` | | phi3:mini | LLM model for refinement |
| `--beam-size` | `-b` | 5 | Beam size for decoding (higher = better quality, slower) |

### Setup Options

| Option | Alias | Description |
|--------|-------|-------------|
| `--all` | `-a` | Setup all dependencies |
| `--ffmpeg` | `-f` | Check FFmpeg availability |
| `--vad` | `-v` | Download Silero VAD model |
| `--whisper` | `-w` | Download specified Whisper model |
| `--ollama` | | Check Ollama availability |

### Examples

```bash
# Transcribe a single file
whathuh -t video.mp4

# Use large-v3 model for best accuracy
whathuh -t video.mp4 -m large-v3

# English only, disable VAD
whathuh -t video.mp4 -l en -n

# Transcribe entire folder with medium model
whathuh -t "C:\Videos" -m medium

# Transcribe MKV files with LLM refinement
whathuh -t *.mkv -r

# Setup all dependencies
whathuh -s -a

# Download large-v3 model
whathuh -s -w large-v3

# Show system info
whathuh -i
```

## Whisper Models

This project uses OpenAI's Whisper model to create the subtitles. There are several model options:

| Model | Description |
|-------|-------------|
| Base | Fast and surprisingly accurate for most use cases |
| Base-en | Base with special English training |
| Small | Better accuracy, more resources |
| Medium | High accuracy, significant resources |
| Large-v3 | Best accuracy, requires significant VRAM |

**Use Large v3 If You Can** - Models are downloaded automatically on first use.

## Runtime Selection

Whisper.net automatically selects the best available runtime:

1. **CUDA** - NVidia GPUs with CUDA drivers
    Requires CUDA 13.1 or later and updated NVIDIA drivers!
2. **Vulkan** - Windows with Vulkan support
3. **CoreML** - Apple Silicon Macs
4. **OpenVINO** - Intel hardware acceleration
5. **CPU** - Universal fallback

## Voice Activity Detection

The Silero VAD integration filters out silence and non-speech audio before transcription. This:
- Reduces Whisper hallucinations during silence
- Speeds up processing by skipping non-speech segments
- Improves overall transcription quality

## LLM Refinement (Optional)

For even better results, enable LLM refinement with Ollama:

1. Install [Ollama](https://ollama.ai)
2. Pull a model: `ollama pull phi3:mini`
3. Enable refinement in GUI or use `--refine` in CLI

The LLM corrects:
- Spelling and grammar errors
- Disfluencies (um, uh, you know)
- Acronym capitalization
- Homophones

## Building from Source

```bash
# Clone the repository
git clone https://github.com/Echostorm44/WhatHuh.git
cd WhatHuh

# Build all projects
dotnet build

# Run GUI
dotnet run --project src/WhatHuh.Gui

# Run CLI
dotnet run --project src/WhatHuh.Cli -- --help
```

## Credits

- [Whisper.net](https://github.com/sandrohanea/whisper.net) by Sandro Hanea
- [Silero VAD](https://github.com/snakers4/silero-vad) by Silero Team
- [Spectre.Console](https://spectreconsole.net/)
- [AvaloniaUI](https://avaloniaui.net/)
- Image icons by rsetiawan - Flaticon

## License

MIT License - See LICENSE for details
