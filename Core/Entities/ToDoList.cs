namespace Core.Entities;

public sealed class ToDoList
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; init; }
    public required ToDoUser User { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
