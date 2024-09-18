using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace relayservice.pulling
{
    public class OutboxMessageService : IOutboxMessageService<OutboxMessage>
    {
        private const string Ready = "Ready";
        private const string InProgress = "InProgress";
        private const string Done = "Done";
        private const string Failed = "Failed";

        private readonly DbContext _dbContext;
        private readonly DbSet<OutboxMessage> _outboxMessages;
        private readonly IMessageBrokerService _messageBrokerService;

        public OutboxMessageService(DbContext dbContext, IMessageBrokerService messageBrokerService)
        {
            _messageBrokerService = messageBrokerService;
            _dbContext = dbContext;
            _outboxMessages = _dbContext.Set<OutboxMessage>();
        }

        public async Task Publish<TEvent>(TEvent @event, Guid correlationId, Header header, byte hash)
        {
            await this._messageBrokerService.Publish(@event, correlationId, header, hash);
        }

        public async Task<OutboxMessage> Next()
        {
            return await Next(outboxMessage => outboxMessage.Status == Ready);
        }

        public async Task<OutboxMessage> Next(byte hash)
        {
            return await Next(outboxMessage => outboxMessage.Status == Ready && outboxMessage.Hash == hash);
        }

        public async Task<OutboxMessage> MarkAsFailed(Guid id, string failedReason)
        {
            return await Mark(id, Failed, failedReason);
        }


        public async Task<OutboxMessage> MarkAsDone(Guid id)
        {
            return await Mark(id, Done);
        }

        public async Task<OutboxMessage> Delete(Guid id)
        {
            var message = await _outboxMessages.FindAsync(id);
            if (message != null)
            {
                _outboxMessages.Remove(message);
                await _dbContext.SaveChangesAsync();
            }
            return message;
        }

        public async Task DeleteMany(IEnumerable<Guid> ids)
        {
            var messages = await _outboxMessages.Where(m => ids.Contains(m.Id)).ToListAsync();
            _outboxMessages.RemoveRange(messages);
            await _dbContext.SaveChangesAsync();
        }

        public async Task MarkAsInProgressMany(IEnumerable<Guid> ids)
        {
            var messages = await _outboxMessages.Where(m => ids.Contains(m.Id)).ToListAsync();
            foreach (var message in messages)
            {
                message.Status = InProgress;
            }
            await _dbContext.SaveChangesAsync();
        }

        private async Task<OutboxMessage> Mark(Guid id, string status, string failedReason = null)
        {
            var message = await _outboxMessages.FindAsync(id);
            if (message != null)
            {
                message.Status = status;
                message.ProcessedDate = DateTime.Now;
                message.FailedReason = failedReason;
                await _dbContext.SaveChangesAsync();
            }
            return message;
        }

        private async Task<OutboxMessage> Next(Func<OutboxMessage, bool> filter)
        {
            var message = await _outboxMessages.Where(filter).OrderBy(t => t.InsertedDate).FirstOrDefaultAsync();
            if (message != null)
            {
                message.Status = InProgress;
                await _dbContext.SaveChangesAsync();
            }
            return message;
        }

        public async Task<IList<OutboxMessage>> NextBatch(byte hash, int batchSize)
        {
            var messages = await _outboxMessages
                .Where(t => t.Status == Ready && t.Hash == hash)
                .OrderBy(t => t.InsertedDate)
                .Take(batchSize)
                .ToListAsync();

            foreach (var message in messages)
            {
                // progressdate 
                message.Status = InProgress;
            }
            await _dbContext.SaveChangesAsync();
            return messages;
        }
    }
}
