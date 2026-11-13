using System.Security.Cryptography;
using CityFy.Api.Auth;
using CityFy.Api.Models;
using CityFy.Api.Repositories;

namespace CityFy.Api.SubServices;

public sealed class UserStoreSubService(IUserRepository repository) : IUserStoreSubService
{
    private const int IdHexLength = 24;

    public string NewUserId() => AuthSetup.AccountUserIdPrefix + RandomNumberGenerator.GetHexString(IdHexLength, lowercase: true);

    public Task<CityFyUser?> GetAsync(string userId, CancellationToken ct) => repository.GetAsync(userId, ct);

    public Task<bool> RecordLoginAsync(string userId, string lastFmUsername, DateTime now, CancellationToken ct) =>
        repository.RecordLoginAsync(userId, lastFmUsername, now, ct);

    public Task<long> DeleteAsync(string userId, CancellationToken ct) => repository.DeleteAsync(userId, ct);

    public Task<IReadOnlyList<string>> ListIdsAsync(CancellationToken ct) => repository.ListIdsAsync(ct);
}
