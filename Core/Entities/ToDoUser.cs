namespace Core.Entities;

public sealed class ToDoUser
{
    public Guid UserId { get; init; } = Guid.NewGuid();
    public long TelegramUserId { get; init; }
    public string? TelegramUserName { get; set; }
}
