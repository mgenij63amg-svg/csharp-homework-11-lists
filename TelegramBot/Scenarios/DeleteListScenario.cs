using Core.Entities;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramBot.Dto;
using TelegramBot.Services;
namespace TelegramBot.Scenarios;

public sealed class DeleteListScenario : IScenario
{
    private readonly IUserService _users;
    private readonly IToDoListService _lists;
    private readonly IToDoService _tasks;
    public DeleteListScenario(IUserService users, IToDoListService lists, IToDoService tasks)
    { _users = users; _lists = lists; _tasks = tasks; }
    public bool CanHandle(ScenarioType scenario) => scenario == ScenarioType.DeleteList;

    public async Task<ScenarioResult> HandleMessageAsync(ITelegramBotClient bot, ScenarioContext context, Update update, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var chat = ScenarioUi.ChatId(update);
        switch (context.CurrentStep)
        {
            case null:
                var user = await _users.GetUser(context.UserId, ct)
                    ?? throw new InvalidOperationException("Пользователь не зарегистрирован.");
                context.Data["User"] = user;
                var choices = await _lists.GetUserLists(user.UserId, ct);
                if (choices.Count == 0)
                {
                    await bot.SendMessage(chat, "У вас нет списков для удаления.", cancellationToken: ct);
                    return ScenarioResult.Completed;
                }
                await bot.SendMessage(chat, "Отменить действие можно командой /cancel.", replyMarkup: BotKeyboards.Cancel, cancellationToken: ct);
                var prompt = await bot.SendMessage(chat, "Выберете список для удаления:",
                    replyMarkup: BotKeyboards.Lists(choices, "deletelist", withoutList: false), cancellationToken: ct);
                ScenarioUi.Remember(context, prompt);
                context.CurrentStep = "Approve";
                return ScenarioResult.Transition;
            case "Approve":
                if (!ScenarioUi.IsCurrent(context, update))
                {
                    await bot.SendMessage(chat, "Выберите список из последнего сообщения или /cancel.", cancellationToken: ct);
                    return ScenarioResult.Transition;
                }
                var dto = ToDoListCallbackDto.FromString(update.CallbackQuery!.Data!);
                if (dto.Action != "deletelist" || dto.ToDoListId is null) return ScenarioResult.Transition;
                var list = await _lists.Get(dto.ToDoListId.Value, ct);
                var owner = (ToDoUser)context.Data["User"];
                if (list is null || list.User.UserId != owner.UserId)
                {
                    await bot.SendMessage(chat, "Список не найден или недоступен.", cancellationToken: ct);
                    return ScenarioResult.Transition;
                }
                context.Data["List"] = list;
                var confirmation = await bot.SendMessage(chat,
                    $"Подтверждаете удаление списка {list.Name} и всех его задач",
                    replyMarkup: BotKeyboards.Confirmation, cancellationToken: ct);
                ScenarioUi.Remember(context, confirmation);
                context.CurrentStep = "Delete";
                return ScenarioResult.Transition;
            case "Delete":
                if (!ScenarioUi.IsCurrent(context, update) || update.CallbackQuery?.Data is not ("yes" or "no"))
                {
                    await bot.SendMessage(chat, "Используйте кнопки Да и Нет из последнего сообщения или /cancel.", cancellationToken: ct);
                    return ScenarioResult.Transition;
                }
                if (update.CallbackQuery.Data == "no")
                {
                    await bot.SendMessage(chat, "Удаление отменено", cancellationToken: ct);
                    return ScenarioResult.Completed;
                }
                var selected = (ToDoList)context.Data["List"];
                var current = await _lists.Get(selected.Id, ct);
                var currentUser = (ToDoUser)context.Data["User"];
                if (current is not null && current.User.UserId == currentUser.UserId)
                {
                    foreach (var item in await _tasks.GetByUserIdAndList(currentUser.UserId, current.Id, ct))
                        await _tasks.Delete(currentUser.UserId, item.Id, ct);
                    await _lists.Delete(current.Id, ct);
                }
                await bot.SendMessage(chat, "Список и все его задачи удалены.", cancellationToken: ct);
                return ScenarioResult.Completed;
            default: throw new InvalidOperationException("Неизвестный шаг удаления списка.");
        }
    }
}
