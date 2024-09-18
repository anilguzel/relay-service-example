using System;
using System.Text;
using System.Threading.Tasks;
using Confluent.Kafka;

namespace relayservice.publisher
{
    public class KafkaMessageBrokerService : IMessageBrokerService
    {
        private readonly IProducer<string, string> _producer;


        public PublisherType PublisherType => PublisherType.Kafka;


        public KafkaMessageBrokerService(IProducer<string, string> producer)
        {
            _producer = producer;
        }

        public async Task Publish<TEvent>(TEvent @event, Guid correlationId, Header header, byte hash)
        {
            string topicName = @event.GetType().ToString();


            Message<string, string> message = new Message<string, string>
            {
                Value = Newtonsoft.Json.JsonConvert.SerializeObject(@event),
                Key = hash.ToString(),
                Timestamp = new Timestamp(header.TimeStamp),
            };
            message.Headers = new Confluent.Kafka.Headers();
            message.Headers.Add("timeStamp", Encoding.ASCII.GetBytes(header.TimeStamp.ToString()));
            message.Headers.Add("userName", Encoding.ASCII.GetBytes(header.UserName ?? ""));
            message.Headers.Add("identityName", Encoding.ASCII.GetBytes(header.IdentityName ?? ""));
            message.Headers.Add("identityType", Encoding.ASCII.GetBytes(header.IdentityType ?? ""));
            message.Headers.Add("version", Encoding.ASCII.GetBytes(header.Version.ToString()));
            message.Headers.Add("tenant", Encoding.ASCII.GetBytes(header.Tenant ?? ""));
            message.Headers.Add("type", Encoding.ASCII.GetBytes(@event.GetType().AssemblyQualifiedName ?? ""));
            message.Headers.Add("hash", Encoding.ASCII.GetBytes(hash.ToString()));
            message.Headers.Add("correlationId", Encoding.ASCII.GetBytes(correlationId.ToString()));


            await _producer.ProduceAsync(topicName, message);
        }
    }
}