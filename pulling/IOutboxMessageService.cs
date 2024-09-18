using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace relayservice.pulling
{
    public interface IOutboxMessageService<T>
    {
        PublisherType PublisherType { get; }
        Task Publish<TEvent>(TEvent @event, Guid correlationId, Header header, byte hash);
        Task<T> Next();
        Task<T> Next(byte hash);
        Task<IList<T>> NextBatch(byte hash, int batchSize);

        Task<T> MarkAsDone(ObjectId id);
        Task<T> MarkAsFailed(ObjectId id, string failedReason);
        Task<T> Delete(ObjectId id);
        Task DeleteMany(IEnumerable<ObjectId> ids);

        Task MarkAsInProgressMany(IEnumerable<ObjectId> ids);

    }
}