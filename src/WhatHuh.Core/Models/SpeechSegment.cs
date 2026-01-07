namespace WhatHuh.Core.Models;

public class SpeechSegment
{
    public int? StartOffset { get; set; }
    public int? EndOffset { get; set; }
    public float? StartSeconds { get; set; }
    public float? EndSeconds { get; set; }

    public SpeechSegment()
    {
    }

    public SpeechSegment(int startOffset, int endOffset, float startSeconds, float endSeconds)
    {
        StartOffset = startOffset;
        EndOffset = endOffset;
        StartSeconds = startSeconds;
        EndSeconds = endSeconds;
    }
}
