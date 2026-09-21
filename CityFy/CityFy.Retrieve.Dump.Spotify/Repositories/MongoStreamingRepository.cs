using System;
using ServiceDefault.Models;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Text.Json;
using CityFy.Retrieve.Dump.Spotify.Models;

namespace CityFy.Retrieve.Dump.Spotify.Repositories
{
    public class MongoStreamingRepository : IStreamingRepository
    {
        private readonly IMongoDatabase _database;
        private readonly MongoOptions _options;

        public MongoStreamingRepository(MongoOptions options, IConfiguration config)
        {
            _options = options;
            var client = new MongoClient(options.ConnectionString);
            _database = client.GetDatabase(options.Database);
        }

        public async Task EnsureCollectionExistsAsync(string collectionName)
        {
            var filter = new BsonDocument("name", collectionName);
            var collections = await _database.ListCollectionsAsync(new ListCollectionsOptions { Filter = filter });
            if (!await collections.AnyAsync())
            {
                await _database.CreateCollectionAsync(collectionName);
            }
        }

        public async Task InsertManyAsync(string collectionName, IEnumerable<StreamingBl> documents, string uploadId)
        {
            var collection = _database.GetCollection<BsonDocument>(collectionName);

            var docs = documents.Select(d =>
            {
                var json = JsonSerializer.Serialize(d);
                var doc = BsonDocument.Parse(json);
                if (!string.IsNullOrWhiteSpace(uploadId))
                    doc.Set("_uploadId", uploadId);
                return doc;
            }).ToList();

            if (docs.Count > 0)
                await collection.InsertManyAsync(docs);
        }

        public async Task CreateTaskAsync(ServiceDefault.Models.ProcessingTask task)
        {
            var collection = _database.GetCollection<ServiceDefault.Models.ProcessingTask>(_options.TaskCollectionName);
            var filter = Builders<ServiceDefault.Models.ProcessingTask>.Filter.Eq(t => t.UploadId, task.UploadId);
            await collection.ReplaceOneAsync(filter, task, new ReplaceOptions { IsUpsert = true });
        }

        public async Task IncrementTaskInsertedAsync(string uploadId, int delta)
        {
            var collection = _database.GetCollection<BsonDocument>(_options.TaskCollectionName);
            var filter = Builders<BsonDocument>.Filter.Eq("UploadId", uploadId);
            var update = Builders<BsonDocument>.Update.Inc("Inserted", delta);
            await collection.UpdateOneAsync(filter, update);
        }

        public async Task UpdateTaskStatusAsync(string uploadId, ServiceDefault.Models.ProcessingStatus status, string? message = null)
        {
            var collection = _database.GetCollection<ServiceDefault.Models.ProcessingTask>(_options.TaskCollectionName);
            var filter = Builders<ServiceDefault.Models.ProcessingTask>.Filter.Eq(t => t.UploadId, uploadId);

            var updateDef = Builders<ServiceDefault.Models.ProcessingTask>.Update.Set(t => t.Status, status);
            if (!string.IsNullOrWhiteSpace(message))
                updateDef = updateDef.Set(t => t.Message, message);
            if (status == ServiceDefault.Models.ProcessingStatus.DONE || status == ServiceDefault.Models.ProcessingStatus.FAILED)
            {
                updateDef = updateDef.Set(t => t.CompletedAt, DateTime.UtcNow);
            }

            await collection.UpdateOneAsync(filter, updateDef);
        }

        public async Task<IEnumerable<ServiceDefault.Models.ProcessingTask>> GetTasksByStatusAsync(ServiceDefault.Models.ProcessingStatus status)
        {
            var collection = _database.GetCollection<ServiceDefault.Models.ProcessingTask>(_options.TaskCollectionName);
            var filter = Builders<ServiceDefault.Models.ProcessingTask>.Filter.Eq(t => t.Status, status);
            return await collection.Find(filter).ToListAsync();
        }

        public async Task<ServiceDefault.Models.ProcessingTask?> GetTaskByUploadIdAsync(string uploadId)
        {
            var collection = _database.GetCollection<ServiceDefault.Models.ProcessingTask>(_options.TaskCollectionName);
            var filter = Builders<ServiceDefault.Models.ProcessingTask>.Filter.Eq(t => t.UploadId, uploadId);
            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<IEnumerable<ServiceDefault.Models.ProcessingTask>> GetTasksPagedAsync(int page, int pageSize)
        {
            var collection = _database.GetCollection<ServiceDefault.Models.ProcessingTask>(_options.TaskCollectionName);
            var skip = (Math.Max(1, page) - 1) * Math.Max(1, pageSize);
            return await collection.Find(Builders<ServiceDefault.Models.ProcessingTask>.Filter.Empty).Skip(skip).Limit(Math.Max(1, pageSize)).ToListAsync();
        }

        public async Task<IEnumerable<string>> GetDocumentsPagedAsync(string collectionName, int page, int pageSize, string? providedUploadId = null)
        {
            var collection = _database.GetCollection<BsonDocument>(collectionName);
            var skip = (Math.Max(1, page) - 1) * Math.Max(1, pageSize);
            FilterDefinition<BsonDocument> filter = FilterDefinition<BsonDocument>.Empty;
            if (!string.IsNullOrWhiteSpace(providedUploadId))
                filter = Builders<BsonDocument>.Filter.Eq("_uploadId", providedUploadId);

            var docs = await collection.Find(filter).Skip(skip).Limit(Math.Max(1, pageSize)).ToListAsync();
            return docs.Select(d => d.ToJson());
        }
    }
}
