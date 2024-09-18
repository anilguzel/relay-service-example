using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace relayservice
{
    public class KafkaWorker : BaseWorker
    {
        public KafkaWorker(IExecutionSetting executionSetting, IEventTypeCacheService eventTypeCacheService,
            IConsoleLogger consoleLogger, Strategies.OutboxResolver resolver) : base(consoleLogger, executionSetting,
            eventTypeCacheService,
            PublisherType.Kafka, resolver)
        {

        }
    }
}