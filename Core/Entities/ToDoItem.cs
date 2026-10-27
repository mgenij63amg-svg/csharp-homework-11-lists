namespace Core.Entities;

public sealed class ToDoItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required ToDoUser User { get; init; }
    public required string Name { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public ToDoList? List { get; init; }
    public DateTime Deadline { get; init; }
    public ToDoItemState State { get; set; } = ToDoItemState.Active;
}
