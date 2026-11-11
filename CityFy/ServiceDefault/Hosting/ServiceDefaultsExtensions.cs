using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using ServiceDefault.Errors;
using ServiceDefault.Logging;

namespace ServiceDefault.Hosting
{
    /// <summary>
    /// Shared bootstrap for every CityFy service (Aspire "ServiceDefaults" pattern):
    /// logging (NLog + OpenTelemetry), tracing/metrics, health checks, service discovery,
    /// CORS and Swagger.
    /// </summary>
    public static class ServiceDefaultsExtensions
    {
        public const string CorsPolicyName = "CityFy";

        private const string HealthEndpointPath = "/health";
        private const string AlivenessEndpointPath = "/alive";

        public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
        {
            // NLog first: AddAppLogging clears the providers, OpenTelemetry must be added after it.
            builder.Services.AddAppLogging(builder.Configuration);
            builder.ConfigureOpenTelemetry();

            // Uniform error responses (ProblemDetails + stable "code" for the frontend)
            builder.Services.AddCityFyProblemDetails();

            // Enums as strings in every JSON response ("FAILED", not 3): readable and stable for the frontend
            builder.Services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(o =>
                o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

            builder.Services.AddHealthChecks()
                .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

            builder.Services.AddServiceDiscovery();
            // Service discovery on every HttpClient. The standard resilience handler is NOT applied
            // globally: its 30s total timeout would break long downloads (MusicBrainz dump) and
            // the Spotify client already retries on its own. Opt in per client with
            // .AddStandardResilienceHandler() where it makes sense (service-to-service calls).
            builder.Services.ConfigureHttpClientDefaults(http => http.AddServiceDiscovery());

            return builder;
        }

        public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
        {
            builder.Logging.AddOpenTelemetry(logging =>
            {
                logging.IncludeFormattedMessage = true;
                logging.IncludeScopes = true;
            });

            builder.Services.AddOpenTelemetry()
                .WithMetrics(metrics => metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation())
                .WithTracing(tracing => tracing
                    .AddSource(builder.Environment.ApplicationName)
                    .AddAspNetCoreInstrumentation(o =>
                        // Don't trace health probes
                        o.Filter = ctx =>
                            !ctx.Request.Path.StartsWithSegments(HealthEndpointPath) &&
                            !ctx.Request.Path.StartsWithSegments(AlivenessEndpointPath))
                    .AddHttpClientInstrumentation());

            // The Aspire AppHost injects OTEL_EXPORTER_OTLP_ENDPOINT (dashboard).
            if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
                builder.Services.AddOpenTelemetry().UseOtlpExporter();

            return builder;
        }

        /// <summary>
        /// CORS policy <see cref="CorsPolicyName"/> with origins from "Cors:AllowedOrigins".
        /// </summary>
        public static IServiceCollection AddCityFyCors(this IServiceCollection services, IConfiguration configuration)
        {
            var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

            services.AddCors(options => options.AddPolicy(CorsPolicyName, policy =>
            {
                if (origins.Length > 0)
                    policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
            }));

            return services;
        }

        /// <summary>
        /// Swagger with two documents: "api" (controllers with an ApiExplorer GroupName)
        /// and "debug" (controllers without a group, i.e. debug/* endpoints).
        /// </summary>
        public static IServiceCollection AddCityFySwagger(this IServiceCollection services, string serviceTitle)
        {
            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("api", new OpenApiInfo { Title = $"{serviceTitle} API", Version = "v1" });
                c.SwaggerDoc("debug", new OpenApiInfo { Title = $"{serviceTitle} Debug", Version = "v1" });
                c.DocInclusionPredicate((docName, apiDesc) =>
                    string.IsNullOrEmpty(apiDesc.GroupName) ? docName == "debug" : docName == "api");
            });

            return services;
        }

        /// <summary>
        /// Standard pipeline: Swagger (Development only), HTTPS redirection, CORS,
        /// /health (all checks) and /alive (liveness only).
        /// </summary>
        public static WebApplication UseServiceDefaults(this WebApplication app)
        {
            // Unhandled exceptions and empty error responses (404, 405, ...) -> ProblemDetails with "code"
            app.UseExceptionHandler();
            app.UseStatusCodePages();

            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI(c =>
                {
                    c.SwaggerEndpoint("/swagger/api/swagger.json", "API");
                    c.SwaggerEndpoint("/swagger/debug/swagger.json", "Debug");
                });
            }

            app.UseHttpsRedirection();
            app.UseCors(CorsPolicyName);

            app.MapHealthChecks(HealthEndpointPath);
            app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains("live")
            });

            return app;
        }
    }
}
