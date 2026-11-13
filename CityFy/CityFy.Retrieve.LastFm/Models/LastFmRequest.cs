using System.Security.Cryptography;
using System.Text;

namespace CityFy.Retrieve.LastFm.Models;

/// <summary>
/// A call to the Last.fm API: method plus parameters (api_key included by the client). Builds the
/// query string and, for authenticated calls, the signature.
/// </summary>
public sealed record LastFmRequest(string Method, IReadOnlyDictionary<string, string> Parameters)
{
    /// <summary>Excluded from the signature (Last.fm authspec).</summary>
    private static readonly HashSet<string> UnsignedParameters = ["format", "callback"];

    public static LastFmRequest Of(string method, params (string Name, string? Value)[] parameters) =>
        new(method, parameters.Where(p => p.Value is not null).ToDictionary(p => p.Name, p => p.Value!, StringComparer.Ordinal));

    /// <summary>
    /// api_sig: parameters sorted by name, "name" + "value" concatenated without separators (method
    /// included, format/callback excluded), the shared secret appended, MD5 as 32 lowercase hex chars.
    /// </summary>
    public string Signature(string sharedSecret)
    {
        var all = Parameters.Append(new KeyValuePair<string, string>("method", Method))
            .Where(p => !UnsignedParameters.Contains(p.Key))
            .OrderBy(p => p.Key, StringComparer.Ordinal);
        var text = new StringBuilder();
        foreach (var (name, value) in all)
            text.Append(name).Append(value);
        text.Append(sharedSecret);
        return Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    /// <summary>"?method=...&amp;name=value..." (values escaped), in a stable order.</summary>
    public string ToQueryString(string? signature = null)
    {
        var parts = Parameters.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}")
            .Prepend($"method={Uri.EscapeDataString(Method)}");
        if (signature is not null)
            parts = parts.Append($"api_sig={signature}");
        return "?" + string.Join('&', parts);
    }
}
