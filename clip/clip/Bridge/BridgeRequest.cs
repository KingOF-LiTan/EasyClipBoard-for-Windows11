using System;
using System.Text.Json;

namespace clip.Bridge;

internal sealed class BridgeRequest : IDisposable
{
    private readonly JsonDocument _document;

    private BridgeRequest(JsonDocument document)
    {
        _document = document;
        Root = document.RootElement;
        Action = Root.GetProperty("action").GetString() ?? "";
        RequestId = Root.TryGetProperty("requestId", out var requestId) ? requestId.GetString() : null;
    }

    public JsonElement Root { get; }

    public string Action { get; }

    public string? RequestId { get; }

    public static BridgeRequest Parse(string jsonMessage)
    {
        return new BridgeRequest(JsonDocument.Parse(jsonMessage));
    }

    public static string? TryGetRequestId(string jsonMessage)
    {
        try
        {
            using var document = JsonDocument.Parse(jsonMessage);
            return document.RootElement.TryGetProperty("requestId", out var requestId)
                ? requestId.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _document.Dispose();
    }
}
