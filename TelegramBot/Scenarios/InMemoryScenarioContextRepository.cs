using System.Collections.Concurrent;
namespace TelegramBot.Scenarios;

public sealed class InMemoryScenarioContextRepository : IScenarioContextRepository
{
    private readonly ConcurrentDictionary<long, ScenarioContext> _contexts = new();
    public Task<ScenarioContext?> GetContext(long userId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_contexts.TryGetValue(userId, out var context) ? context : null);
    }
    public Task SetContext(long userId, ScenarioContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(context);
        if (context.UserId != userId) throw new ArgumentException("Контекст относится к другому пользователю.");
        _contexts[userId] = context;
        return Task.CompletedTask;
    }
    public Task ResetContext(long userId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _contexts.TryRemove(userId, out _);
        return Task.CompletedTask;
    }
}
