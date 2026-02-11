using System;
using System.Collections.Generic;

namespace clip.Core.Models;

public sealed record ClipboardItemDraft(
    ClipboardItemType Type,
    DateTimeOffset CapturedAtUtc,
    long EstimatedBytes,
    string? Text = null,
    byte[]? ImagePng = null,
    int? ImageWidth = null,
    int? ImageHeight = null,
    IReadOnlyList<string>? FilePaths = null,
    ClipboardItemTag Tag = ClipboardItemTag.Temporary,
    bool isSensitive = false,
    SensitiveType SensitiveType = SensitiveType.None,
    string? Username = null
);
