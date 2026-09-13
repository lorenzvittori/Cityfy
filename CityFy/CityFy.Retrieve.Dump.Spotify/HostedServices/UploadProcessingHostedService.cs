using CityFy.Retrieve.Dump.Spotify.FileClients;
using CityFy.Retrieve.Dump.Spotify.Models;
using CityFy.Retrieve.Dump.Spotify.Services;
using Microsoft.Extensions.Options;

namespace CityFy.Retrieve.Dump.Spotify.HostedServices
{
    public class UploadProcessingHostedService : BackgroundService
    {
        private readonly ILogger<UploadProcessingHostedService> _logger;
        private readonly IFileClient _fileClient;
        private readonly IServiceProvider _servicesProvider;
        private readonly DebugUploadOptions _options;

        private readonly TimeSpan _interval = TimeSpan.FromMinutes(1);

        public UploadProcessingHostedService(ILogger<UploadProcessingHostedService> logger,
            IFileClient fileClient,
            IServiceProvider servicesProvider,
            IOptions<DebugUploadOptions> options)
        {
            _logger = logger;
            _fileClient = fileClient;
            _servicesProvider = servicesProvider;
            _options = options.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("UploadProcessingHostedService started, interval {Interval}.", _interval);

            var uploadsRoot = _fileClient.GetFullPath("uploads");
            _fileClient.CreateDirectory(uploadsRoot);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Query tasks with pending status and process those

                    using var scopeAll = _servicesProvider.CreateScope();
                    var persistence = scopeAll.ServiceProvider.GetRequiredService<IStreamingBlPersistenceService>();
                    var pendingTasks = await persistence.GetTasksByStatusAsync("pending");

                    foreach (var task in pendingTasks)
                    {
                        if (stoppingToken.IsCancellationRequested) break;

                        var uploadId = task.UploadId;
                        var dir = _fileClient.GetFullPath("uploads", uploadId);
                        var extracted = Path.Combine(dir, "extracted");
                        var marker = Path.Combine(dir, ".processed");

                        _logger.LogInformation("Found pending task for upload {UploadId}", uploadId);

                        if (!Directory.Exists(extracted))
                        {
                            _logger.LogInformation("Extracted folder not found for upload {UploadId}, skipping", uploadId);
                            continue;
                        }

                        if (File.Exists(marker))
                        {
                            _logger.LogInformation("Upload {UploadId} already processed (marker exists), marking task completed", uploadId);
                            try { await persistence.UpdateTaskStatusAsync(uploadId, "completed"); } catch { }
                            continue;
                        }

                        try
                        {
                            // create a scope to resolve scoped services per upload processing
                            using var scope = _servicesProvider.CreateScope();
                            var streamingService = scope.ServiceProvider.GetRequiredService<IStreamingHistoryService>();
                            var persistenceService = scope.ServiceProvider.GetRequiredService<IStreamingBlPersistenceService>();

                            // create task in mongo
                            try
                            {
                                await persistenceService.CreateTaskAsync(uploadId);
                                await persistenceService.UpdateTaskStatusAsync(uploadId, "running");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to create task for {UploadId}", uploadId);
                            }

                            await streamingService.ProcessExtractedFilesAsync(uploadId, extracted);

                            // mark task completed
                            try
                            {
                                await persistenceService.UpdateTaskStatusAsync(uploadId, "completed");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to update task status to completed for {UploadId}", uploadId);
                            }

                            // create marker file to avoid reprocessing
                            try
                            {
                                File.WriteAllText(marker, DateTime.UtcNow.ToString("o"));
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to write processed marker for {UploadId}", uploadId);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to process upload {UploadId}", uploadId);
                            try
                            {
                                using var scope = _servicesProvider.CreateScope();
                                var persistenceService = scope.ServiceProvider.GetRequiredService<IStreamingBlPersistenceService>();
                                await persistenceService.UpdateTaskStatusAsync(uploadId, "failed", ex.Message);
                            }
                            catch (Exception e)
                            {
                                _logger.LogWarning(e, "Failed to update task status to failed for {UploadId}", uploadId);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while scanning uploads");
                }

                await Task.Delay(_interval, stoppingToken);
            }

            _logger.LogInformation("UploadProcessingHostedService stopping");
        }
    }
}
