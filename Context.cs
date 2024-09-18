public interface IContext
{
    IExecutionSetting ExecutionSetting { get; }
    IEventTypeCacheService EventTypeCacheService { get; }
    IOutboxMessageService<OutboxMessage> OutboxMessageService { get; }
    CancellationToken StoppingToken { get; }
    byte Hash { get; }
    int TryCount { get; }
    bool HasOutboxMessage { get; }

    void SetOutboxMessages(IEnumerable<OutboxMessage> messages);
    void IncreaseTryCount();
    void ResetTryCount();
    void ResetPublishedIdList();
    void AddPublishedId(Guid messageId);
}

public class Context : IContext
{
    public IExecutionSetting ExecutionSetting { get; }
    public IEventTypeCacheService EventTypeCacheService { get; }
    public IOutboxMessageService<OutboxMessage> OutboxMessageService { get; }
    public CancellationToken StoppingToken { get; }
    public byte Hash { get; }
    public int TryCount { get; private set; }
    public bool HasOutboxMessage => OutboxMessages?.Any() == true;
    public List<OutboxMessage> OutboxMessages { get; private set; }
    public List<Guid> PublishedMessageIds { get; private set; } = new List<Guid>();

    public Context(IExecutionSetting executionSetting,
                   IEventTypeCacheService eventTypeCacheService,
                   IOutboxMessageService<OutboxMessage> outboxMessageService,
                   CancellationToken stoppingToken,
                   byte hash)
    {
        ExecutionSetting = executionSetting;
        EventTypeCacheService = eventTypeCacheService;
        OutboxMessageService = outboxMessageService;
        StoppingToken = stoppingToken;
        Hash = hash;
        TryCount = 0;
    }

    public void SetOutboxMessages(IEnumerable<OutboxMessage> messages)
    {
        OutboxMessages = messages.ToList();
    }

    public void IncreaseTryCount()
    {
        TryCount++;
    }

    public void ResetTryCount()
    {
        TryCount = 0;
    }

    public void ResetPublishedIdList()
    {
        PublishedMessageIds.Clear();
    }

    public void AddPublishedId(Guid messageId)
    {
        PublishedMessageIds.Add(messageId);
    }
}
