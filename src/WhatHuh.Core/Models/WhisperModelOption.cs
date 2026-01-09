using Whisper.net.Ggml;

namespace WhatHuh.Core.Models;

public class WhisperModelOption
{
    public GgmlType EnumType { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long ExpectedSizeBytes { get; set; }

    public static List<WhisperModelOption> GetDefaultOptions() =>
    [
        new WhisperModelOption { EnumType = GgmlType.Base, DisplayName = "Base", FileName = "ggml-base.bin", ExpectedSizeBytes = 147_000_000 },
        new WhisperModelOption { EnumType = GgmlType.BaseEn, DisplayName = "Base English Only", FileName = "ggml-base-en.bin", ExpectedSizeBytes = 147_000_000 },
        new WhisperModelOption { EnumType = GgmlType.Small, DisplayName = "Small", FileName = "ggml-sm.bin", ExpectedSizeBytes = 488_000_000 },
        new WhisperModelOption { EnumType = GgmlType.SmallEn, DisplayName = "Small English Only", FileName = "ggml-sm-en.bin", ExpectedSizeBytes = 488_000_000 },
        new WhisperModelOption { EnumType = GgmlType.Medium, DisplayName = "Medium", FileName = "ggml-med.bin", ExpectedSizeBytes = 1_530_000_000 },
        new WhisperModelOption { EnumType = GgmlType.MediumEn, DisplayName = "Medium English Only", FileName = "ggml-med-en.bin", ExpectedSizeBytes = 1_530_000_000 },
        new WhisperModelOption { EnumType = GgmlType.LargeV1, DisplayName = "Large v1", FileName = "ggml-lg1.bin", ExpectedSizeBytes = 3_090_000_000 },
        new WhisperModelOption { EnumType = GgmlType.LargeV2, DisplayName = "Large v2", FileName = "ggml-lg2.bin", ExpectedSizeBytes = 3_090_000_000 },
        new WhisperModelOption { EnumType = GgmlType.LargeV3, DisplayName = "Large v3", FileName = "ggml-lgv3.bin", ExpectedSizeBytes = 3_090_000_000 },
    ];
}
