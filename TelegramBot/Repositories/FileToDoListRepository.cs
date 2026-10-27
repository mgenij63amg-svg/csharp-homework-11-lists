using Core.Entities;
namespace TelegramBot.Repositories;

public sealed class FileToDoListRepository : IToDoListRepository
{
    private readonly JsonFileStore<ToDoList> _store;
    public FileToDoListRepository(string directory) => _store = new(directory, "lists.json");
    public async Task<ToDoList?> Get(Guid id, CancellationToken ct) =>
        (await _store.Read(ct)).FirstOrDefault(x => x.Id == id);
    public async Task<IReadOnlyList<ToDoList>> GetByUserId(Guid userId, CancellationToken ct) =>
        (await _store.Read(ct)).Where(x => x.User.UserId == userId).OrderBy(x => x.CreatedAt).ToArray();
    public async Task<bool> ExistsByName(Guid userId, string name, CancellationToken ct) =>
        (await GetByUserId(userId, ct)).Any(x => SameName(x.Name, name));
    private static bool SameName(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    public async Task Add(ToDoList list, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(list);
        await _store.Change(values =>
        {
            if (values.Any(x => x.Id == list.Id || x.User.UserId == list.User.UserId && SameName(x.Name, list.Name)))
                throw new ArgumentException("Список с таким названием уже существует.");
            values.Add(list);
            return true;
        }, ct);
    }
    public async Task Delete(Guid id, CancellationToken ct) =>
        _ = await _store.Change(values => values.RemoveAll(x => x.Id == id), ct);
}
