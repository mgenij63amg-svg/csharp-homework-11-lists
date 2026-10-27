namespace TelegramBot.Dto;

public sealed class ToDoListCallbackDto : CallbackDto
{
    public Guid? ToDoListId { get; set; }
    public static new ToDoListCallbackDto FromString(string input)
    {
        Validate(input);
        var parts = input.Split('|');
        Guid? id = null;
        if (parts.Length > 1 && parts[1].Length > 0)
        {
            if (!Guid.TryParse(parts[1], out var parsed)) throw new FormatException("Некорректный идентификатор списка.");
            id = parsed;
        }
        return new() { Action = parts[0], ToDoListId = id };
    }
    public override string ToString()
    {
        var value = $"{base.ToString()}|{ToDoListId}";
        Validate(value);
        return value;
    }
}
