using System.Globalization;
using Core.Entities;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramBot.Dto;
using TelegramBot.Services;
namespace TelegramBot.Scenarios;

public sealed class AddTaskScenario : IScenario
{
    private readonly IUserService _users;
    private readonly IToDoService _tasks;
    private readonly IToDoListService _lists;
    public AddTaskScenario(IUserService users, IToDoService tasks, IToDoListService lists)
    { _users = users; _tasks = tasks; _lists = lists; }
    public bool CanHandle(ScenarioType scenario) => scenario == ScenarioType.AddTask;

    private async Task SelectList(ITelegramBotClient bot, ScenarioContext context, long chat, CancellationToken ct)
    {
        var user = (ToDoUser)context.Data["User"];
        var prompt = await bot.SendMessage(chat, "Выберите список:",
            replyMarkup: BotKeyboards.Lists(await _lists.GetUserLists(user.UserId, ct), "tasklist"), cancellationToken: ct);
        ScenarioUi.Remember(context, prompt);
    }

    public async Task<ScenarioResult> HandleMessageAsync(ITelegramBotClient bot, ScenarioContext context, Update update, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var chat = ScenarioUi.ChatId(update);
        switch (context.CurrentStep)
        {
            case null:
                context.Data["User"] = await _users.GetUser(context.UserId, ct)
                    ?? throw new InvalidOperationException("Пользователь не зарегистрирован.");
                await bot.SendMessage(chat, "Введите название задачи:", replyMarkup: BotKeyboards.Cancel, cancellationToken: ct);
                context.CurrentStep = "Name";
                return ScenarioResult.Transition;
            case "Name":
                var name = update.Message?.Text?.Trim();
                if (string.IsNullOrWhiteSpace(name) || name.Length > ToDoService.MaxNameLength || name.StartsWith('/'))
                {
                    await bot.SendMessage(chat, "Введите название задачи текстом от 1 до 128 символов или /cancel.",
                        replyMarkup: BotKeyboards.Cancel, cancellationToken: ct);
                    return ScenarioResult.Transition;
                }
                await bot.SendMessage(chat, "Введите срок выполнения (dd.MM.yyyy):", replyMarkup: BotKeyboards.Cancel, cancellationToken: ct);
                context.Data["Name"] = name;
                context.CurrentStep = "Deadline";
                return ScenarioResult.Transition;
            case "Deadline":
                var date = update.Message?.Text;
                if (date is null || date.Length != 10 || !DateTime.TryParseExact(date, "dd.MM.yyyy",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var deadline))
                {
                    await bot.SendMessage(chat, "Неверный формат даты. Введите существующую дату в формате dd.MM.yyyy:",
                        replyMarkup: BotKeyboards.Cancel, cancellationToken: ct);
                    return ScenarioResult.Transition;
                }
                context.Data["Deadline"] = deadline;
                await SelectList(bot, context, chat, ct);
                context.CurrentStep = "List";
                return ScenarioResult.Transition;
            case "List":
                if (!ScenarioUi.IsCurrent(context, update))
                {
                    await bot.SendMessage(chat, "Выберите список кнопкой из последнего сообщения или /cancel.", cancellationToken: ct);
                    return ScenarioResult.Transition;
                }
                var dto = ToDoListCallbackDto.FromString(update.CallbackQuery!.Data!);
                if (dto.Action != "tasklist") return ScenarioResult.Transition;
                var user = (ToDoUser)context.Data["User"];
                ToDoList? list = null;
                if (dto.ToDoListId.HasValue)
                {
                    list = await _lists.Get(dto.ToDoListId.Value, ct);
                    if (list is null || list.User.UserId != user.UserId)
                    {
                        await bot.SendMessage(chat, "Список недоступен. Выберите другой список.", cancellationToken: ct);
                        await SelectList(bot, context, chat, ct);
                        return ScenarioResult.Transition;
                    }
                }
                if (!context.Data.TryGetValue("CreatedTask", out var savedTask))
                {
                    savedTask = await _tasks.Add(user, (string)context.Data["Name"], (DateTime)context.Data["Deadline"], list, ct);
                    context.Data["CreatedTask"] = savedTask;
                }
                var item = (ToDoItem)savedTask;
                await bot.SendMessage(chat,
                    $"Задача {item.Name} успешно добавлена.\nСрок: {item.Deadline.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}\nСписок: {item.List?.Name ?? "Без списка"}",
                    cancellationToken: ct);
                return ScenarioResult.Completed;
            default: throw new InvalidOperationException("Неизвестный шаг добавления задачи.");
        }
    }
}
