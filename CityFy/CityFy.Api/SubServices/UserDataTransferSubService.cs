using CityFy.Api.Clients;
using ServiceDefault.Errors;

namespace CityFy.Api.SubServices;

public sealed class UserDataTransferSubService(
    IDumpClient dump,
    ICatalogClient catalog,
    IOrchestratorClient orchestrator,
    ICityGeneratorClient cityGenerator,
    ILogger<UserDataTransferSubService> logger) : IUserDataTransferSubService
{
    private const string PlaysKey = "plays";

    public async Task<IReadOnlyList<string>> TransferEverywhereAsync(string fromUserId, string toUserId, CancellationToken ct)
    {
        var failed = new List<string>();
        long plays = 0;

        // Original data: uploads (an import still running leaves the transfer incomplete), then the plays
        await StepAsync("RetrieveDumpSpotify", failed, async () => (await dump.TransferUserDataAsync(fromUserId, toUserId, ct)).Complete);
        await StepAsync("Catalog", failed, async () =>
        {
            plays = (await catalog.TransferUserDataAsync(fromUserId, toUserId, ct)).Moved.GetValueOrDefault(PlaysKey);
            return true;
        });

        // Derived data of the old owner: not moved, rebuilt for the new one
        await StepAsync("Orchestrator", failed, async () => { await orchestrator.DeleteUserDataAsync(fromUserId, ct); return true; });
        await StepAsync("CityGenerator", failed, async () => { await cityGenerator.DeleteUserDataAsync(fromUserId, ct); return true; });
        if (plays > 0)
            await StepAsync("Orchestrator", failed, async () => { await orchestrator.TriggerAsync(toUserId, ct); return true; });

        return failed.Distinct().ToList();
    }

    /// <summary>A step that fails or is not complete yet is recorded and the next ones still run.</summary>
    private async Task StepAsync(string service, List<string> failed, Func<Task<bool>> step)
    {
        try
        {
            if (await step())
                return;
            logger.LogInformation("Data transfer: {Service} not complete yet, retried later", service);
        }
        catch (CityFyException ex)
        {
            logger.LogWarning("Data transfer: {Service} failed ({Code}) {Detail}", service, ex.Code, ex.Message);
        }
        failed.Add(service);
    }
}
