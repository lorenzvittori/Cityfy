using System.Text.Json.Nodes;

namespace CityFy.Api.SubServices;

public interface IRedactionSubService
{
    /// <summary>Replaces (in place, recursively) the values of fields whose name looks like a secret. Returns the same node.</summary>
    JsonNode? Redact(JsonNode? node);
}
