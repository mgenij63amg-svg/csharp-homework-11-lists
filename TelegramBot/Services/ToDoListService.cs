using System.Globalization;
using Core.Entities;
using TelegramBot.Repositories;
namespace TelegramBot.Services;

public sealed class ToDoListService : IToDoListService
{
    public const int MaxNameLength = 10;
    private readonly IToDoListRepository _repository;
    public ToDoListService(IToDoListRepository repository) => _repository = repository;
    public async Task<ToDoList> Add(ToDoUser user, string name, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(user);
        var normalized = name?.Trim() ?? "";
        if (normalized.Length == 0 || new StringInfo(normalized).LengthInTextElements > MaxNameLength || normalized.StartsWith('/'))
            throw new ArgumentException("Название списка должно содержать от 1 до 10 символов.");
        if (await _repository.ExistsByName(user.UserId, normalized, ct))
            throw new ArgumentException("Список с таким названием уже существует.");
        var list = new ToDoList { Name = normalized, User = user };
        await _repository.Add(list, ct);
        return list;
    }
    public Task<ToDoList?> Get(Guid id, CancellationToken ct) => _repository.Get(id, ct);
    public Task Delete(Guid id, CancellationToken ct) => _repository.Delete(id, ct);
    public Task<IReadOnlyList<ToDoList>> GetUserLists(Guid userId, CancellationToken ct) => _repository.GetByUserId(userId, ct);
}
