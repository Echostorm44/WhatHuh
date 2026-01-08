# What? Huh? Subtitler - Improvement Plan

## Code Improvements

### High Priority
- Add cancellation token support throughout the pipeline to allow users to stop long-running transcriptions
- Display detected hardware acceleration in the GUI status area (CUDA, QSV, AMF) so users know GPU is being utilized
- Add error handling around GPU-accelerated ffmpeg calls with automatic fallback to CPU if hardware decoding fails

### Medium Priority
- Add progress reporting for Whisper model downloads showing actual file size instead of just bytes downloaded
- Add keyboard shortcuts (Delete to remove selected file from queue, Ctrl+O to browse)
- Show estimated time remaining based on processing speed of completed files

## Apple-Style GUI Improvements

### Visual Design
- Replace #0A84FF accent color with a softer blue gradient (#007AFF to #5856D6) for a more macOS feel
- Use SF Pro Display font family if available, falling back to Segoe UI on Windows
- Add hover states with subtle scale transforms (1.02x) on interactive elements
- Use thinner, lighter font weights (300 for body, 500 for headings) matching Apple typography

### Micro-interactions
- Add smooth fade-in animation when files are added to queue
- Implement an indeterminate progress shimmer animation during model loading

### Components
- Add tooltip popovers on hover explaining each option in plain language

## New Feature Ideas

### Transcription
- Add speaker diarization to identify and label different speakers in the transcript
- Support for real-time transcription from microphone input
- Add translation mode to transcribe audio in one language and output subtitles in another
- Implement word-level timestamps for karaoke-style subtitle display

### Export
- Add VTT and ASS subtitle format export options in addition to SRT
- Generate a plain text transcript file alongside the SRT
- Add "copy to clipboard" button for quick transcript sharing
- Implement export presets (YouTube, Netflix, custom timing rules)

### Integration
- Add watch folder mode that automatically processes new videos dropped into a configured directory
- Implement Ollama model auto-pull if the selected LLM model is not installed
- Add system tray mode for background processing with notifications on completion
- Support for network paths and cloud storage (OneDrive, Google Drive) file sources
