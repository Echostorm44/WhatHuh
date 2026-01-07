using System.Globalization;
using System.Text;
using WhatHuh.Core.Models;

namespace WhatHuh.Core.Utilities;

public static class SrtWriter
{
    private const int MaxLineLength = 42;

    public static string FormatTime(TimeSpan time)
    {
        return time.ToString(@"hh\:mm\:ss\,fff", CultureInfo.InvariantCulture);
    }

    public static string FormatSegment(TranscriptionResult result)
    {
        var wrappedText = WrapText(result.Text, MaxLineLength);
        return $"{result.Sequence}\n{FormatTime(result.Start)} --> {FormatTime(result.End)}\n{wrappedText}\n";
    }

    public static async Task WriteAsync(string path, IEnumerable<TranscriptionResult> results)
    {
        await using var writer = new StreamWriter(path, false, Encoding.UTF8);
        
        foreach (var result in results)
        {
            await writer.WriteLineAsync(result.Sequence.ToString());
            await writer.WriteLineAsync($"{FormatTime(result.Start)} --> {FormatTime(result.End)}");
            await writer.WriteLineAsync(WrapText(result.Text, MaxLineLength));
            await writer.WriteLineAsync();
        }
    }

    public static void Write(string path, IEnumerable<TranscriptionResult> results)
    {
        using var writer = new StreamWriter(path, false, Encoding.UTF8);
        
        foreach (var result in results)
        {
            writer.WriteLine(result.Sequence);
            writer.WriteLine($"{FormatTime(result.Start)} --> {FormatTime(result.End)}");
            writer.WriteLine(WrapText(result.Text, MaxLineLength));
            writer.WriteLine();
        }
    }

    private static string WrapText(string text, int maxLineLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLineLength)
            return text;

        var result = new StringBuilder();
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var currentLine = new StringBuilder();

        foreach (var word in words)
        {
            if (currentLine.Length == 0)
            {
                currentLine.Append(word);
            }
            else if (currentLine.Length + 1 + word.Length <= maxLineLength)
            {
                currentLine.Append(' ');
                currentLine.Append(word);
            }
            else
            {
                if (result.Length > 0)
                    result.Append('\n');
                result.Append(currentLine);
                currentLine.Clear();
                currentLine.Append(word);
            }
        }

        if (currentLine.Length > 0)
        {
            if (result.Length > 0)
                result.Append('\n');
            result.Append(currentLine);
        }

        return result.ToString();
    }
}
