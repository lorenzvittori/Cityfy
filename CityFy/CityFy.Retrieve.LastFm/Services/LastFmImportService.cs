using CityFy.Retrieve.LastFm.Models;
using CityFy.Retrieve.LastFm.SubServices;
using ServiceDefault.Errors;
using ServiceDefault.Models;

namespace CityFy.Retrieve.LastFm.Services;

public sealed class LastFmImportService(
    ILastFmImportJobSubService imports,
    ILastFmAccountStoreSubService accounts,
    ILastFmScrobbleReadSubService scrobbles,
    IListeningImportedEventSubService importedEvents,
    ILastFmErrorSubService errors,
    ILogger<LastFmImportService> logger) : ILastFmImportService
{
    public async Task RunAsync(LastFmImportJob job, CancellationToken stoppingToken)
    {
        if (await imports.TryStartAsync(job, stoppingToken) is not { } cts)
        {
            logger.LogInformation("Last.fm import {ImportId} was cancelled or deleted before starting", job.ImportId);
            return;
        }

        var ct = cts.Token;
        try
        {
            var account = await accounts.GetRequiredAsync(job.UserId, ct);
            var sessionKey = accounts.GetSessionKey(account);
            var state = account.HistoryImport is { } saved && saved.ImportId == job.ImportId
                ? saved
                : new LastFmHistoryImport(job.ImportId, account.LastScrobbleAt, NextPage: 1, TotalPages: null, Inserted: 0);

            while (state.TotalPages is null || state.NextPage <= state.TotalPages)
            {
                var page = await scrobbles.ReadHistoryPageAsync(account, sessionKey, state, ct);
                state = state with { NextPage = state.NextPage + 1, TotalPages = page.TotalPages, Inserted = state.Inserted + page.Inserted };
                // Resume point first: a restart from here does not read this page again
                await accounts.SetHistoryImportAsync(job.UserId, state, CancellationToken.None);
                await imports.ProgressAsync(job.ImportId, state.NextPage - 1, Math.Max(page.TotalPages, 1), state.Inserted);
                if (page.Total == 0)
                    break; // empty history: Last.fm answers totalPages 0
            }

            if (state.Inserted > 0)
                await importedEvents.PublishAsync(job.UserId, job.ImportId, state.Inserted, ct);
            await imports.CompleteAsync(job.ImportId, state.Inserted);
            logger.LogInformation("Last.fm import {ImportId} done for user {UserId}: {Pages} pages, {Inserted} new plays",
                job.ImportId, job.UserId, state.TotalPages, state.Inserted);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Left InProgress on purpose: the recovery resumes it from the saved page at startup
            logger.LogInformation("Last.fm import {ImportId} interrupted by the shutdown, it resumes at the next start", job.ImportId);
            throw;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Cancelled (status already written); the plays stored so far stay
            logger.LogInformation("Last.fm import {ImportId} cancelled", job.ImportId);
        }
        catch (Exception ex)
        {
            var code = errors.ToErrorCode(ex, ErrorCodes.LastFmImportFailed);
            if (errors.IsConfigurationProblem(ex))
                logger.LogCritical(ex, "Last.fm refused the API key of CityFy: check LastFm:ApiKey");
            else
                logger.LogError(ex, "Last.fm import {ImportId} failed ({Code})", job.ImportId, code);

            if (errors.IsAccountProblem(code))
                await accounts.SuspendAsync(job.UserId, code, CancellationToken.None);
            await imports.FailAsync(job.ImportId, ex.Message, code);
        }
        finally
        {
            imports.End(job.ImportId, cts);
        }
    }

    public async Task CancelAsync(string importId, CancellationToken ct)
    {
        var previous = await imports.CancelAsync(importId, ct);
        logger.LogInformation("Last.fm import {ImportId} cancelled ({Status})", importId, previous);
    }

    public Task<ProcessingTask?> GetAsync(string importId, CancellationToken ct) => imports.GetAsync(importId, ct);

    public Task<IReadOnlyList<ProcessingTask>> ListAsync(int limit, CancellationToken ct) => imports.ListAsync(limit, ct);

    public async Task<int> RequeueUnfinishedAsync(CancellationToken ct)
    {
        var count = await imports.RequeueUnfinishedAsync(ct);
        if (count > 0)
            logger.LogInformation("Re-queued {Count} unfinished Last.fm imports", count);
        return count;
    }
}
