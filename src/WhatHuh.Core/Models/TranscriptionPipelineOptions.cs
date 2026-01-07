namespace WhatHuh.Core.Models;

public class TranscriptionPipelineOptions
{
    public string AppPath { get; set; } = string.Empty;
    public WhisperModelOption Model { get; set; } = null!;
    public string Language { get; set; } = "auto";
    public bool UseVad { get; set; } = true;
    public bool UseLlmRefinement { get; set; } = false;
    public string LlmModel { get; set; } = "phi3:mini";
    public int BeamSize { get; set; } = 5;
}
