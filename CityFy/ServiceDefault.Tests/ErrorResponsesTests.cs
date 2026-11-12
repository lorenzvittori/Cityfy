using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ServiceDefault.Errors;
using ServiceDefault.Hosting;

namespace ServiceDefault.Tests;

[ApiController]
[Route("test")]
public sealed class ErrorTestController : ControllerBase
{
    [HttpGet("cityfy")]
    public IActionResult ThrowCityFy() => throw new CityFyException(ErrorCodes.UploadNotZip, "Only .zip files are accepted");

    [HttpGet("crash")]
    public IActionResult Crash() => throw new InvalidOperationException("secret internal detail");

    [HttpGet("problem")]
    public IActionResult Problem() => this.CityFyProblem(ErrorCodes.CityNotReady, "not built", StatusCodes.Status404NotFound);

    [HttpGet("validate")]
    public IActionResult Validate([FromQuery] int number) => Ok(number);
}

/// <summary>The error contract with the frontend: ProblemDetails with "code" and "traceId".</summary>
public sealed class ErrorResponsesTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.AddServiceDefaults();
        builder.Services.AddControllers().AddApplicationPart(typeof(ErrorTestController).Assembly);

        _app = builder.Build();
        _app.UseServiceDefaults();
        _app.MapControllers();
        await _app.StartAsync(TestContext.Current.CancellationToken);
        _client = _app.GetTestClient();
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    private async Task<(HttpStatusCode Status, JsonElement Body)> GetAsync(string path)
    {
        using var resp = await _client.GetAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal("application/problem+json", resp.Content.Headers.ContentType?.MediaType);
        var json = await resp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return (resp.StatusCode, JsonDocument.Parse(json).RootElement);
    }

    [Fact]
    public async Task CityFyException_BecomesItsCodeAndStatus()
    {
        var (status, body) = await GetAsync("/test/cityfy");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(ErrorCodes.UploadNotZip, body.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task UnexpectedException_Is500_WithoutLeakingDetailsOutsideDevelopment()
    {
        var (status, body) = await GetAsync("/test/crash");

        Assert.Equal(HttpStatusCode.InternalServerError, status);
        Assert.Equal(ErrorCodes.InternalError, body.GetProperty("code").GetString());
        Assert.DoesNotContain("secret", body.GetRawText());
    }

    [Fact]
    public async Task ControllerProblem_KeepsItsCode()
    {
        var (status, body) = await GetAsync("/test/problem");

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal(ErrorCodes.CityNotReady, body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task UnknownRoute_IsNotFound()
    {
        var (status, body) = await GetAsync("/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal(ErrorCodes.NotFound, body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ModelValidationError_IsValidationFailed()
    {
        var (status, body) = await GetAsync("/test/validate?number=abc");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(ErrorCodes.ValidationFailed, body.GetProperty("code").GetString());
    }
}
