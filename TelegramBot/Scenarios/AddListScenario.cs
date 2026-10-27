using Core.Entities;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramBot.Services;
namespace TelegramBot.Scenarios;

public sealed class AddListScenario : IScenario
{
    private readonly IUserService _users;
    private readonly IToDoListService _lists;
    public AddListScenario(IUserService users, IToDoListService lists) { _users = users; _lists = lists; }
    public bool CanHandle(ScenarioType scenario) => scenario == ScenarioType.AddList;
    public async Task<ScenarioResult> HandleMessageAsync(ITelegramBotClient bot, ScenarioContext context, Update update, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var chat = ScenarioUi.ChatId(update);
        switch (context.CurrentStep)
        {
            case null:
                context.Data["User"] = await _users.GetUser(context.UserId, ct)
                    ?? throw new InvalidOperationException("Пользователь не зарегистрирован.");
                await bot.SendMessage(chat, "Введите название списка:", replyMarkup: BotKeyboards.Cancel, cancellationToken: ct);
                context.CurrentStep = "Name";
                return ScenarioResult.Transition;
            case "Name":
                if (update.Message?.Text is not string name)
                {
                    await bot.SendMessage(chat, "Введите название списка текстом или /cancel.", cancellationToken: ct);
                    return ScenarioResult.Transition;
                }
                ToDoList list;
                try
                {
                    if (context.Data.TryGetValue("CreatedList", out var saved)) list = (ToDoList)saved;
                    else
                    {
                        list = await _lists.Add((ToDoUser)context.Data["User"], name, ct);
                        context.Data["CreatedList"] = list;
                    }
                }
                catch (ArgumentException ex)
                {
                    await bot.SendMessage(chat, ex.Message + " Введите другое название:", replyMarkup: BotKeyboards.Cancel, cancellationToken: ct);
                    return ScenarioResult.Transition;
                }
                await bot.SendMessage(chat, $"Список {list.Name} успешно добавлен.", cancellationToken: ct);
                return ScenarioResult.Completed;
            default: throw new InvalidOperationException("Неизвестный шаг добавления списка.");
        }
    }
}
