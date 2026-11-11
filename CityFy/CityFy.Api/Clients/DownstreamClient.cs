using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using ServiceDefault.Errors;

namespace CityFy.Api.Clients;

/// <summary>
/// Base of the clients towards the internal services. A downstream error becomes a
/// <see cref="CityFyException"/> with the same code and status, so the shared error handler
/// returns to the browser exactly the code of the ERRORI_UI.md catalog. An unreachable service
/// becomes SERVICE_UNAVAILABLE (503).
/// </summary>
public abstract class DownstreamClient
{
    protected static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected readonly HttpClient Http;
    private readonly string _serviceName;

    protected DownstreamClient(HttpClient http, string serviceName)
    {
        Http = http;
        _serviceName = serviceName;
    }

    protected async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await SendRawAsync(request, ct);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct)
            ?? throw new CityFyException(ErrorCodes.InternalError, $"{_serviceName}: empty response", StatusCodes.Status502BadGateway);
    }

    protected async Task SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var _ = await SendRawAsync(request, ct);
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpRequestMessage request, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await Http.SendAsync(request, ct);
        }
        catch (Exception ex) when (IsUnreachable(ex, ct))
        {
            throw new CityFyException(ErrorCodes.ServiceUnavailable, $"{_serviceName} is not reachable", StatusCodes.Status503ServiceUnavailable, ex);
        }

        if (response.IsSuccessStatusCode)
            return response;

        using (response)
            throw await ToExceptionAsync(response, ct);
    }

    internal async Task<CityFyException> ToExceptionAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var status = (int)response.StatusCode;
        string? code = null, detail = null;

        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions, ct);
            detail = problem?.Detail;
            if (problem?.Extensions.TryGetValue(ErrorResponses.CodeExtension, out var raw) == true)
                code = raw is JsonElement { ValueKind: JsonValueKind.String } e ? e.GetString() : raw?.ToString();
        }
        catch (JsonException)
        {
            // not a ProblemDetails body
        }

        code ??= status switch
        {
            StatusCodes.Status404NotFound => ErrorCodes.NotFound,
            StatusCodes.Status503ServiceUnavailable or StatusCodes.Status502BadGateway or StatusCodes.Status504GatewayTimeout => ErrorCodes.ServiceUnavailable,
            >= 500 => ErrorCodes.InternalError,
            _ => ErrorCodes.RequestRejected
        };

        return new CityFyException(code, $"{_serviceName}: {detail ?? response.ReasonPhrase}", status);
    }

    private static bool IsUnreachable(Exception ex, CancellationToken ct) => ex switch
    {
        HttpRequestException => true,
        TaskCanceledException => !ct.IsCancellationRequested, // client timeout, not the caller cancelling
        Polly.CircuitBreaker.BrokenCircuitException or Polly.Timeout.TimeoutRejectedException => true,
        _ => false
    };
}
