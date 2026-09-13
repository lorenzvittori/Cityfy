using System.Collections.Generic;
using System.Threading.Tasks;

namespace ServiceDefault.Interfaces
{
    public interface IStorageRepository<T>
    {
        Task InsertManyAsync(string collectionName, IEnumerable<T> documents, string uploadId);
        Task EnsureCollectionExistsAsync(string collectionName);
    }
}
