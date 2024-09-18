using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;

namespace relayservice
{
    public abstract class BaseWorker : BackgroundService
    {
        private readonly IConsoleLogger _consoleLogger;
        private readonly IExecutionSetting _executionSetting;
        private readonly IEventTypeCacheService _eventTypeCacheService;
        private readonly IOutboxMessageService<OutboxMessage> _outboxMessageService;
        private readonly Strategies.OutboxResolver _resolver;


        public BaseWorker(IConsoleLogger consoleLogger, IExecutionSetting executionSetting,
            IEventTypeCacheService eventTypeCacheService, PublisherType publisherType, Strategies.OutboxResolver resolver)
        {
            _consoleLogger = consoleLogger;
            _executionSetting = executionSetting;
            _eventTypeCacheService = eventTypeCacheService;
            _resolver = resolver;

            var (messageBrokerService, collection) = _resolver(publisherType);
            _outboxMessageService = new OutboxMessageService(collection, publisherType, messageBrokerService);
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            List<Task> tasks = new List<Task>();

            for (byte hash = default; hash < _executionSetting.ConcurrencyLimit; hash++)
            {
                var tempHash = hash;
                var task = await Task.Factory.StartNew(async () =>
                {
                    await _action(new Context(_executionSetting,
                        _eventTypeCacheService,
                        _outboxMessageService,
                        stoppingToken,
                        tempHash));
                }, stoppingToken);

                tasks.Add(task);
            }

            await Task.WhenAll(tasks);
        }

        protected async Task _action(object c)
        {
            if (!(c is IContext context)) return;
            while (!context.StoppingToken.IsCancellationRequested)
                try
                // hash yerine dl kullan, nextBatch okurken lock atsin, okuduklarini guncellesin, sonra release et sonraki ceksin
                {
                    context.SetOutboxMessages(
                        await context.OutboxMessageService.NextBatch(context.Hash, context.ExecutionSetting.BatchSize));

                    if (!context.HasOutboxMessage)
                    {
                        context.IncreaseTryCount();
                        if (context.TryCount >= context.ExecutionSetting.MaxTryCount)
                            await Task.Delay(context.ExecutionSetting.MillisecondsDelay);

                        continue;
                    }
                    // has failed outbox message -- elle yonet
                    // uzun sure inprgress kalir mi, kalmaz delete ediyoruz


                    context.ResetTryCount();
                    context.ResetPublishedIdList();


                    await context.OutboxMessageService.MarkAsInProgressMany(
                        context.OutboxMessages.Select(x => x.Id));


                    foreach (var outboxMessage in context.OutboxMessages)
                    {
                        try
                        {
                            await context.OutboxMessageService.Publish(
                                JsonConvert.DeserializeObject(outboxMessage.Data,
                                    context.EventTypeCacheService.GetOrCreate(outboxMessage.Type)),
                                outboxMessage.CorrelationId,
                                outboxMessage.Header,
                                outboxMessage.Hash);

                            context.AddPublishedId(outboxMessage.Id);
                        }
                        catch (Exception e)
                        {
                            await context.OutboxMessageService.MarkAsFailed(outboxMessage.Id, e.ToString());
                            _consoleLogger.LogError(e.Message, e);
                        }
                    }


                    await context.OutboxMessageService.DeleteMany(context.PublishedMessageIds);
                }
                catch (Exception e)
                {
                    _consoleLogger.LogError(e.Message, e);
                }
        }
    }
}