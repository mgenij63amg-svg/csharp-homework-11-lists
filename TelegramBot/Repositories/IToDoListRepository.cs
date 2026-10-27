using Core.Entities;
namespace TelegramBot.Repositories;

public interface IToDoListRepository
{
    Task<ToDoList?> Get(Guid id, CancellationToken ct);
    Task<IReadOnlyList<ToDoList>> GetByUserId(Guid userId, CancellationToken ct);
    Task Add(ToDoList list, CancellationToken ct);
    Task Delete(Guid id, CancellationToken ct);
    Task<bool> ExistsByName(Guid userId, string name, CancellationToken ct);
}
