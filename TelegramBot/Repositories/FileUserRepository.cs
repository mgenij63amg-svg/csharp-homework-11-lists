using Core.Entities;
namespace TelegramBot.Repositories;

public sealed class FileUserRepository
{
    private readonly JsonFileStore<ToDoUser> _store;
    public FileUserRepository(string directory) => _store = new(directory, "users.json");
    public async Task<ToDoUser?> Get(long telegramId, CancellationToken ct) =>
        (await _store.Read(ct)).FirstOrDefault(x => x.TelegramUserId == telegramId);
    public Task<ToDoUser> Register(long telegramId, string? name, CancellationToken ct) => _store.Change(values =>
    {
        var user = values.FirstOrDefault(x => x.TelegramUserId == telegramId);
        if (user is null)
        {
            user = new ToDoUser { TelegramUserId = telegramId };
            values.Add(user);
        }
        user.TelegramUserName = name;
        return user;
    }, ct);
}
