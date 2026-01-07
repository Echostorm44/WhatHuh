namespace WhatHuh.Core.Models;

public class TranscriptionResult
{
    public int Sequence { get; set; }
    public TimeSpan Start { get; set; }
    public TimeSpan End { get; set; }
    public string Text { get; set; } = string.Empty;
}
