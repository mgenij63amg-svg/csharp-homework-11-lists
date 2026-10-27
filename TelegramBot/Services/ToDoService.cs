using Core.Entities;
using TelegramBot.Repositories;
namespace TelegramBot.Services;

public sealed class ToDoService : IToDoService
{
    public const int MaxNameLength = 128;
    private readonly JsonFileStore<ToDoItem> _store;
    private readonly IToDoListService _lists;
    public ToDoService(string directory, IToDoListService lists)
    {
        _store = new(directory, "tasks.json");
        _lists = lists;
    }
    public async Task<ToDoItem> Add(ToDoUser user, string name, DateTime deadline, ToDoList? list, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(user);
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaxNameLength)
            throw new ArgumentException("Название задачи должно содержать от 1 до 128 символов.");
        if (list is not null)
        {
            var actual = await _lists.Get(list.Id, ct);
            if (actual is null || actual.User.UserId != user.UserId)
                throw new ArgumentException("Список не найден или принадлежит другому пользователю.");
            list = actual;
        }
        var item = new ToDoItem { User = user, Name = name.Trim(), Deadline = deadline.Date, List = list };
        await _store.Change(values => { values.Add(item); return true; }, ct);
        return item;
    }
    public async Task<IReadOnlyList<ToDoItem>> GetAllByUserId(Guid userId, CancellationToken ct) =>
        (await _store.Read(ct)).Where(x => x.User.UserId == userId).OrderBy(x => x.CreatedAt).ToArray();
    public async Task<IReadOnlyList<ToDoItem>> GetByUserIdAndList(Guid userId, Guid? listId, CancellationToken ct) =>
        (await GetAllByUserId(userId, ct)).Where(x => x.List?.Id == listId).ToArray();
    public Task<bool> Complete(Guid userId, Guid taskId, CancellationToken ct) => _store.Change(values =>
    {
        var item = values.FirstOrDefault(x => x.Id == taskId && x.User.UserId == userId);
        if (item is null) return false;
        item.State = ToDoItemState.Completed;
        return true;
    }, ct);
    public Task<bool> Delete(Guid userId, Guid taskId, CancellationToken ct) =>
        _store.Change(values => values.RemoveAll(x => x.Id == taskId && x.User.UserId == userId) != 0, ct);
}
