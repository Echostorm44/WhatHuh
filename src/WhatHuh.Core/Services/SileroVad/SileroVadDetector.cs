using NAudio.Wave;
using WhatHuh.Core.Models;

namespace WhatHuh.Core.Services.SileroVad;

public class SileroVadDetector : IDisposable
{
    private readonly SileroVadOnnxModel Model;
    private readonly float Threshold;
    private readonly float NegThreshold;
    private readonly int SamplingRate;
    private readonly int WindowSizeSample;
    private readonly float MinSpeechSamples;
    private readonly float SpeechPadSamples;
    private readonly float MaxSpeechSamples;
    private readonly float MinSilenceSamples;
    private readonly float MinSilenceSamplesAtMaxSpeech;
    private int AudioLengthSamples;
    private const float ThresholdGap = 0.15f;

    public SileroVadDetector(
        string onnxModelPath, 
        float threshold = 0.5f, 
        int samplingRate = 16000,
        int minSpeechDurationMs = 250, 
        float maxSpeechDurationSeconds = 30f,
        int minSilenceDurationMs = 100, 
        int speechPadMs = 30)
    {
        if (samplingRate != 8000 && samplingRate != 16000)
        {
            throw new ArgumentException("Sampling rate not supported, only available for [8000, 16000]");
        }

        Model = new SileroVadOnnxModel(onnxModelPath);
        SamplingRate = samplingRate;
        Threshold = threshold;
        NegThreshold = threshold - ThresholdGap;
        WindowSizeSample = samplingRate == 16000 ? 512 : 256;
        MinSpeechSamples = samplingRate * minSpeechDurationMs / 1000f;
        SpeechPadSamples = samplingRate * speechPadMs / 1000f;
        MaxSpeechSamples = samplingRate * maxSpeechDurationSeconds - WindowSizeSample - 2 * SpeechPadSamples;
        MinSilenceSamples = samplingRate * minSilenceDurationMs / 1000f;
        MinSilenceSamplesAtMaxSpeech = samplingRate * 98 / 1000f;
    }

    public void Reset()
    {
        Model.ResetStates();
    }

    public void Dispose()
    {
        Model.Dispose();
        GC.SuppressFinalize(this);
    }

    public List<SpeechSegment> GetSpeechSegments(string wavFilePath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Reset();

        using var audioFile = new AudioFileReader(wavFilePath);
        var speechProbList = new List<float>();
        AudioLengthSamples = (int)(audioFile.Length / 2);
        var buffer = new float[WindowSizeSample];

        long totalSamples = audioFile.Length / 2;

        while (audioFile.Read(buffer, 0, buffer.Length) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            float speechProb = Model.Call([ buffer ], SamplingRate)[0];
            speechProbList.Add(speechProb);
            progress?.Report((double)audioFile.Position / totalSamples);
        }

        return CalculateProb(speechProbList);
    }

    public List<SpeechSegment> GetSpeechSegments(Stream audioStream,
        CancellationToken cancellationToken = default)
    {
        Reset();

        using var audioFile = new WaveFileReader(audioStream);
        var sampleProvider = audioFile.ToSampleProvider();
        var speechProbList = new List<float>();
        AudioLengthSamples = (int)(audioStream.Length / 2);
        var buffer = new float[WindowSizeSample];

        while (sampleProvider.Read(buffer, 0, buffer.Length) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            float speechProb = Model.Call([ buffer ], SamplingRate)[0];
            speechProbList.Add(speechProb);
        }

        return CalculateProb(speechProbList);
    }

    private List<SpeechSegment> CalculateProb(List<float> speechProbList)
    {
        var result = new List<SpeechSegment>();
        bool triggered = false;
        int tempEnd = 0, prevEnd = 0, nextStart = 0;
        var segment = new SpeechSegment();

        for (int i = 0;i < speechProbList.Count;i++)
        {
            float speechProb = speechProbList[i];
            if (speechProb >= Threshold && tempEnd != 0)
            {
                tempEnd = 0;
                if (nextStart < prevEnd)
                {
                    nextStart = WindowSizeSample * i;
                }
            }

            if (speechProb >= Threshold && !triggered)
            {
                triggered = true;
                segment.StartOffset = WindowSizeSample * i;
                continue;
            }

            if (triggered && (WindowSizeSample * i) - segment.StartOffset > MaxSpeechSamples)
            {
                if (prevEnd != 0)
                {
                    segment.EndOffset = prevEnd;
                    result.Add(segment);
                    segment = new SpeechSegment();
                    if (nextStart < prevEnd)
                    {
                        triggered = false;
                    }
                    else
                    {
                        segment.StartOffset = nextStart;
                    }

                    prevEnd = 0;
                    nextStart = 0;
                    tempEnd = 0;
                }
                else
                {
                    segment.EndOffset = WindowSizeSample * i;
                    result.Add(segment);
                    segment = new SpeechSegment();
                    prevEnd = 0;
                    nextStart = 0;
                    tempEnd = 0;
                    triggered = false;
                    continue;
                }
            }

            if (speechProb < NegThreshold && triggered)
            {
                if (tempEnd == 0)
                {
                    tempEnd = WindowSizeSample * i;
                }

                if ((WindowSizeSample * i) - tempEnd > MinSilenceSamplesAtMaxSpeech)
                {
                    prevEnd = tempEnd;
                }

                if ((WindowSizeSample * i) - tempEnd < MinSilenceSamples)
                {
                    continue;
                }
                else
                {
                    segment.EndOffset = tempEnd;
                    if ((segment.EndOffset - segment.StartOffset) > MinSpeechSamples)
                    {
                        result.Add(segment);
                    }

                    segment = new SpeechSegment();
                    prevEnd = 0;
                    nextStart = 0;
                    tempEnd = 0;
                    triggered = false;
                    continue;
                }
            }
        }

        if (segment.StartOffset != null && (AudioLengthSamples - segment.StartOffset) > MinSpeechSamples)
        {
            segment.EndOffset = speechProbList.Count * WindowSizeSample;
            result.Add(segment);
        }

        ApplySpeechPadding(result);
        return MergeAndCalculateSeconds(result, SamplingRate);
    }

    private void ApplySpeechPadding(List<SpeechSegment> result)
    {
        for (int i = 0;i < result.Count;i++)
        {
            var item = result[i];
            if (i == 0)
            {
                item.StartOffset = (int)Math.Max(0, item.StartOffset!.Value - SpeechPadSamples);
            }

            if (i != result.Count - 1)
            {
                var nextItem = result[i + 1];
                int silenceDuration = nextItem.StartOffset!.Value - item.EndOffset!.Value;
                if (silenceDuration < 2 * SpeechPadSamples)
                {
                    item.EndOffset += silenceDuration / 2;
                    nextItem.StartOffset = Math.Max(0, nextItem.StartOffset.Value - (silenceDuration / 2));
                }
                else
                {
                    item.EndOffset = (int)Math.Min(AudioLengthSamples, item.EndOffset.Value + SpeechPadSamples);
                    nextItem.StartOffset = (int)Math.Max(0, nextItem.StartOffset.Value - SpeechPadSamples);
                }
            }
            else
            {
                item.EndOffset = (int)Math.Min(AudioLengthSamples, item.EndOffset!.Value + SpeechPadSamples);
            }
        }
    }

    private static List<SpeechSegment> MergeAndCalculateSeconds(List<SpeechSegment> original, int samplingRate)
    {
        var result = new List<SpeechSegment>();
        if (original == null || original.Count == 0)
        {
            return result;
        }

        int left = original[0].StartOffset!.Value;
        int right = original[0].EndOffset!.Value;

        if (original.Count > 1)
        {
            original.Sort((a, b) => a.StartOffset!.Value.CompareTo(b.StartOffset!.Value));
            for (int i = 1;i < original.Count;i++)
            {
                var segment = original[i];

                if (segment.StartOffset > right)
                {
                    result.Add(new SpeechSegment(left, right,
                        CalculateSecondByOffset(left, samplingRate), CalculateSecondByOffset(right, samplingRate)));
                    left = segment.StartOffset!.Value;
                    right = segment.EndOffset!.Value;
                }
                else
                {
                    right = Math.Max(right, segment.EndOffset!.Value);
                }
            }

            result.Add(new SpeechSegment(left, right,
                CalculateSecondByOffset(left, samplingRate), CalculateSecondByOffset(right, samplingRate)));
        }
        else
        {
            result.Add(new SpeechSegment(left, right,
                CalculateSecondByOffset(left, samplingRate), CalculateSecondByOffset(right, samplingRate)));
        }

        return result;
    }

    private static float CalculateSecondByOffset(int offset, int samplingRate)
    {
        float secondValue = offset * 1.0f / samplingRate;
        return (float)Math.Floor(secondValue * 1000.0f) / 1000.0f;
    }
}
