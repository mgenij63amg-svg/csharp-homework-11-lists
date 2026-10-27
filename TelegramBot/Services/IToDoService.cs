using Core.Entities;
namespace TelegramBot.Services;

public interface IToDoService
{
    Task<ToDoItem> Add(ToDoUser user, string name, DateTime deadline, ToDoList? list, CancellationToken ct);
    Task<IReadOnlyList<ToDoItem>> GetAllByUserId(Guid userId, CancellationToken ct);
    Task<IReadOnlyList<ToDoItem>> GetByUserIdAndList(Guid userId, Guid? listId, CancellationToken ct);
    Task<bool> Complete(Guid userId, Guid taskId, CancellationToken ct);
    Task<bool> Delete(Guid userId, Guid taskId, CancellationToken ct);
}
