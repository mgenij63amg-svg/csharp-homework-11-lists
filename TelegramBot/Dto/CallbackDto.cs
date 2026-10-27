using System.Text;
namespace TelegramBot.Dto;

public class CallbackDto
{
    public string Action { get; set; } = "";
    protected static void Validate(string input)
    {
        if (string.IsNullOrEmpty(input) || Encoding.UTF8.GetByteCount(input) > 64)
            throw new FormatException("Некорректные данные кнопки.");
    }
    public static CallbackDto FromString(string input)
    {
        Validate(input);
        return new() { Action = input.Split('|')[0] };
    }
    public override string ToString()
    {
        Validate(Action);
        return Action;
    }
}
