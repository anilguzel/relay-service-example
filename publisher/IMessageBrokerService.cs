using System;
using System.Threading.Tasks;

namespace relayservice.publisher
{
    public interface IMessageBrokerService
    {
        Task Publish<TEvent>(TEvent @event, Guid correlationId, Header header, byte hash);
        PublisherType PublisherType { get; }
    }

    public interface IOmsBus : IBus
    {

    }
}