using CityFy.Retrieve.Spotify.Models;
using MongoDB.Driver;

namespace CityFy.Retrieve.Spotify.Repositories
{
    public class MongoSpotifyAccountRepository : ISpotifyAccountRepository
    {
        private const string CollectionName = "spotify_accounts";

        private readonly IMongoCollection<SpotifyAccount> _accounts;

        public MongoSpotifyAccountRepository(IMongoDatabase db)
        {
            _accounts = db.GetCollection<SpotifyAccount>(CollectionName);
        }

        public Task UpsertAsync(SpotifyAccount account, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var update = Builders<SpotifyAccount>.Update
                .Set(a => a.DisplayName, account.DisplayName)
                .Set(a => a.ProtectedRefreshToken, account.ProtectedRefreshToken)
                .Set(a => a.Scope, account.Scope)
                .Set(a => a.UpdatedAt, now)
                .SetOnInsert(a => a.LinkedAt, now);

            return _accounts.UpdateOneAsync(a => a.UserId == account.UserId, update,
                new UpdateOptions { IsUpsert = true }, ct);
        }

        public async Task<SpotifyAccount?> GetAsync(string userId, CancellationToken ct) =>
            await _accounts.Find(a => a.UserId == userId).FirstOrDefaultAsync(ct);

        public Task UpdateRefreshTokenAsync(string userId, string protectedRefreshToken, CancellationToken ct) =>
            _accounts.UpdateOneAsync(a => a.UserId == userId,
                Builders<SpotifyAccount>.Update
                    .Set(a => a.ProtectedRefreshToken, protectedRefreshToken)
                    .Set(a => a.UpdatedAt, DateTime.UtcNow),
                cancellationToken: ct);

        public Task UpdateRetrieveCursorAsync(string userId, DateTime retrievedAt, DateTime? lastPlayedAt, CancellationToken ct)
        {
            var update = Builders<SpotifyAccount>.Update.Set(a => a.LastRetrieveAt, retrievedAt);
            if (lastPlayedAt.HasValue)
                update = update.Max(a => a.LastPlayedAt, lastPlayedAt.Value);

            return _accounts.UpdateOneAsync(a => a.UserId == userId, update, cancellationToken: ct);
        }

        public async Task<IReadOnlyList<SpotifyAccount>> ListAsync(CancellationToken ct) =>
            await _accounts.Find(Builders<SpotifyAccount>.Filter.Empty)
                .Project<SpotifyAccount>(Builders<SpotifyAccount>.Projection.Exclude(a => a.ProtectedRefreshToken))
                .SortByDescending(a => a.LinkedAt)
                .ToListAsync(ct);

        public async Task<IReadOnlyList<string>> GetAllUserIdsAsync(CancellationToken ct) =>
            await _accounts.Find(Builders<SpotifyAccount>.Filter.Empty)
                .Project(a => a.UserId)
                .ToListAsync(ct);
    }
}
