using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WhatHuh.Core.Services;

public class LlmRefinementService
{
    private readonly HttpClient HttpClient;
    private readonly string ModelName;
    private const string OllamaBaseUrl = "http://localhost:11434";

    public LlmRefinementService(string modelName = "phi3:mini")
    {
        HttpClient = new HttpClient
        {
            BaseAddress = new Uri(OllamaBaseUrl),
            Timeout = TimeSpan.FromMinutes(5)
        };
        ModelName = modelName;
    }

    public async Task<string> RefineTranscriptAsync(string rawText, CancellationToken cancellationToken = default)
    {
        var prompt = BuildRefinementPrompt(rawText);

        var request = new OllamaRequest
        {
            Model = ModelName,
            Prompt = prompt,
            Stream = false,
            Options = new OllamaOptions
            {
                Temperature = 0.3,
                TopP = 0.9
            }
        };

        var json = JsonSerializer.Serialize(request, OllamaJsonContext.Default.OllamaRequest);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await HttpClient.PostAsync("/api/generate", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize(responseJson, OllamaJsonContext.Default.OllamaResponse);
        return result?.Response?.Trim() ?? rawText;
    }

    public async Task<List<Models.TranscriptionResult>> RefineSegmentsAsync(
        List<Models.TranscriptionResult> segments,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var refinedSegments = new List<Models.TranscriptionResult>();
        var totalSegments = segments.Count;

        for (int i = 0;i < segments.Count;i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var segment = segments[i];
            var refinedText = await RefineTranscriptAsync(segment.Text, cancellationToken);

            refinedSegments.Add(new Models.TranscriptionResult
            {
                Sequence = segment.Sequence,
                Start = segment.Start,
                End = segment.End,
                Text = refinedText
            });

            progress?.Report((double)(i + 1) / totalSegments);
        }

        return refinedSegments;
    }

    public async Task<List<Models.TranscriptionResult>> RefineBatchAsync(
        List<Models.TranscriptionResult> segments,
        int batchSize = 10,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var refinedSegments = new List<Models.TranscriptionResult>();
        var batches = segments.Chunk(batchSize).ToList();
        var processedBatches = 0;

        foreach (var batch in batches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var combinedText = string.Join("\n", batch.Select(s => 
                $"[{s.Sequence}] {s.Text}"));
            var prompt = BuildBatchRefinementPrompt(combinedText);

            var request = new OllamaRequest
            {
                Model = ModelName,
                Prompt = prompt,
                Stream = false,
                Options = new OllamaOptions
                {
                    Temperature = 0.3,
                    TopP = 0.9
                }
            };

            var json = JsonSerializer.Serialize(request, OllamaJsonContext.Default.OllamaRequest);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await HttpClient.PostAsync("/api/generate", content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
                var result = JsonSerializer.Deserialize(responseJson, OllamaJsonContext.Default.OllamaResponse);
                var refinedLines = ParseBatchResponse(result?.Response ?? "", 
                    batch.ToList());
                refinedSegments.AddRange(refinedLines);
            }
            else
            {
                refinedSegments.AddRange(batch);
            }

            processedBatches++;
            progress?.Report((double)processedBatches / batches.Count);
        }

        return refinedSegments;
    }

    private static string BuildRefinementPrompt(string text)
    {
        return $"""
            You are a transcript editor. Your task is to clean up the following speech transcription.
            
            Rules:
            1. Fix spelling errors and grammar mistakes
            2. Remove disfluencies like "um", "uh", "you know", "like" (when used as filler)
            3. Fix capitalization, especially for proper nouns and acronyms
            4. Preserve the original phrasing as much as possible
            5. Only return the corrected text, nothing else
            
            Original text:
            {text}
            
            Corrected text:
            """;
    }

    private static string BuildBatchRefinementPrompt(string text)
    {
        return $"""
            You are a transcript editor. Clean up the following transcription segments.
            Each segment is prefixed with [number]. Maintain the same format in your response.
            
            Rules:
            1. Fix spelling errors and grammar mistakes
            2. Remove disfluencies like "um", "uh", "you know", "like" (when used as filler)
            3. Fix capitalization, especially for proper nouns and acronyms
            4. Preserve the original phrasing
            5. Keep the [number] prefix for each line
            
            Segments:
            {text}
            
            Corrected segments:
            """;
    }

    private static List<Models.TranscriptionResult> ParseBatchResponse(
        string response, List<Models.TranscriptionResult> original)
    {
        var results = new List<Models.TranscriptionResult>();
        var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var lineDict = new Dictionary<int, string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('['))
            {
                var closeBracket = trimmed.IndexOf(']');
                if (closeBracket > 1 && int.TryParse(trimmed[1..closeBracket], 
                    out var sequence))
                {
                    var text = trimmed[(closeBracket + 1)..].Trim();
                    lineDict[sequence] = text;
                }
            }
        }

        foreach (var segment in original)
        {
            results.Add(new Models.TranscriptionResult
            {
                Sequence = segment.Sequence,
                Start = segment.Start,
                End = segment.End,
                Text = lineDict.TryGetValue(segment.Sequence, out var refined) 
                    ? refined : segment.Text
            });
        }

        return results;
    }

    public static async Task<bool> IsAvailableAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var response = await client.GetAsync($"{OllamaBaseUrl}/api/tags");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<List<string>> GetAvailableModelsAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var response = await client.GetAsync($"{OllamaBaseUrl}/api/tags");

            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            var content = await response.Content.ReadAsStringAsync();
            var models = new List<string>();

            var nameIndex = 0;
            while ((nameIndex = content.IndexOf("\"name\":\"", nameIndex, 
                StringComparison.Ordinal)) != -1)
            {
                nameIndex += 8;
                var endIndex = content.IndexOf("\"", nameIndex, 
                    StringComparison.Ordinal);
                if (endIndex != -1)
                {
                    models.Add(content[nameIndex..endIndex]);
                    nameIndex = endIndex;
                }
            }

            return models;
        }
        catch
        {
            return [];
        }
    }

    public static async Task<bool> PullModelAsync(string modelName, IProgress<string>? status = null, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            
            var request = new { name = modelName, stream = true };
            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{OllamaBaseUrl}/api/pull")
            {
                Content = content
            };
            
            using var response = await client.SendAsync(requestMessage, 
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);
            
            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                if (string.IsNullOrEmpty(line)) continue;
                
                // Parse status from JSON response
                var statusIndex = line.IndexOf("\"status\":\"", StringComparison.Ordinal);
                if (statusIndex != -1)
                {
                    statusIndex += 10;
                    var endIndex = line.IndexOf("\"", statusIndex, StringComparison.Ordinal);
                    if (endIndex != -1)
                    {
                        var pullStatus = line[statusIndex..endIndex];
                        status?.Report(pullStatus);
                    }
                }
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> IsModelAvailableAsync(string modelName)
    {
        var models = await GetAvailableModelsAsync();
        return models.Any(m => m.Equals(modelName, StringComparison.OrdinalIgnoreCase) ||
                               m.StartsWith(modelName + ":", StringComparison.OrdinalIgnoreCase));
    }

}

internal class OllamaResponse
{
    [JsonPropertyName("response")]
    public string? Response { get; set; }
}

internal class OllamaRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "";
    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = "";
    [JsonPropertyName("stream")]
    public bool Stream { get; set; }
    [JsonPropertyName("options")]
    public OllamaOptions Options { get; set; } = new();
}

internal class OllamaOptions
{
    [JsonPropertyName("temperature")]
    public double Temperature { get; set; }
    [JsonPropertyName("top_p")]
    public double TopP { get; set; }
}

[JsonSerializable(typeof(OllamaRequest))]
[JsonSerializable(typeof(OllamaResponse))]
[JsonSerializable(typeof(OllamaOptions))]
internal partial class OllamaJsonContext : JsonSerializerContext
{
}
