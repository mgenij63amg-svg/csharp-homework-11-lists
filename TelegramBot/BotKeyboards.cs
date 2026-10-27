using Core.Entities;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramBot.Dto;
namespace TelegramBot;

public static class BotKeyboards
{
    public static ReplyKeyboardMarkup Main => new(new[]
    {
        new KeyboardButton[] { new("/addtask"), new("/show"), new("/report") }
    }) { ResizeKeyboard = true };
    public static ReplyKeyboardMarkup Cancel => new(new[]
    {
        new KeyboardButton[] { new("/cancel") }
    }) { ResizeKeyboard = true };

    public static InlineKeyboardMarkup Lists(IReadOnlyList<ToDoList> lists, string action,
        bool withoutList = true, bool management = false)
    {
        var rows = new List<InlineKeyboardButton[]>();
        if (withoutList) rows.Add(new[] { InlineKeyboardButton.WithCallbackData("📌Без списка",
            new ToDoListCallbackDto { Action = action }.ToString()) });
        foreach (var list in lists)
            rows.Add(new[] { InlineKeyboardButton.WithCallbackData(list.Name,
                new ToDoListCallbackDto { Action = action, ToDoListId = list.Id }.ToString()) });
        if (management) rows.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData("🆕Добавить", "addlist"),
            InlineKeyboardButton.WithCallbackData("❌Удалить", "deletelist")
        });
        return new(rows);
    }
    public static InlineKeyboardMarkup Confirmation => new(new[]
    {
        new[] { InlineKeyboardButton.WithCallbackData("✅Да", "yes"), InlineKeyboardButton.WithCallbackData("❌Нет", "no") }
    });
}
