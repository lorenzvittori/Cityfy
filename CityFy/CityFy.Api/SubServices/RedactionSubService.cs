using System.Text.Json.Nodes;

namespace CityFy.Api.SubServices;

public sealed class RedactionSubService : IRedactionSubService
{
    private const string Redacted = "«redacted»";
    private static readonly string[] SecretMarkers = ["token", "secret", "password", "pwd"];

    public JsonNode? Redact(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (SecretMarkers.Any(m => key.Contains(m, StringComparison.OrdinalIgnoreCase)))
                        obj[key] = Redacted;
                    else
                        Redact(obj[key]);
                }
                break;
            case JsonArray arr:
                foreach (var item in arr)
                    Redact(item);
                break;
        }
        return node;
    }
}
