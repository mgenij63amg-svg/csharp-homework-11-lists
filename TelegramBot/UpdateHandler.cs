using System.Collections.Concurrent;
using System.Globalization;
using Core.Entities;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramBot.Dto;
using TelegramBot.Scenarios;
using TelegramBot.Services;
namespace TelegramBot;

public sealed class UpdateHandler
{
    private readonly ITelegramBotClient _bot;
    private readonly IUserService _users;
    private readonly IToDoService _tasks;
    private readonly IToDoListService _lists;
    private readonly IReadOnlyList<IScenario> _scenarios;
    private readonly IScenarioContextRepository _contexts;
    private readonly ConcurrentDictionary<long, SemaphoreSlim> _userGates = new();

    public UpdateHandler(ITelegramBotClient bot, IUserService users, IToDoService tasks,
        IEnumerable<IScenario> scenarios, IScenarioContextRepository contextRepository, IToDoListService lists)
    { _bot = bot; _users = users; _tasks = tasks; _scenarios = scenarios.ToArray(); _contexts = contextRepository; _lists = lists; }

    public async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var sender = update.Message?.From ?? update.CallbackQuery?.From;
        var chat = update.Message?.Chat ?? update.CallbackQuery?.Message?.Chat;
        if (sender is null || sender.IsBot || chat?.Type != ChatType.Private || chat.Id != sender.Id) return;
        var gate = _userGates.GetOrAdd(sender.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            switch (update)
            {
                case { Message: { } message }: await OnMessage(update, message, ct); break;
                case { CallbackQuery: { } query }: await OnCallbackQuery(update, query, ct); break;
            }
        }
        finally { gate.Release(); }
    }

    private async Task OnMessage(Update update, Message message, CancellationToken ct)
    {
        var id = message.From!.Id;
        var text = message.Text?.Trim() ?? "";
        var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var command = parts.FirstOrDefault()?.Split('@')[0].ToLowerInvariant() ?? "";
        var argument = parts.Length > 1 ? parts[1].Trim() : "";
        var context = await _contexts.GetContext(id, ct);
        if (command == "/cancel")
        {
            await _contexts.ResetContext(id, ct);
            await Reply(id, context is null ? "Нет активного сценария." : "Текущий сценарий отменён.", ct);
            return;
        }
        if (context is not null) { await ProcessScenario(context, update, ct); return; }
        if (command == "/start")
        {
            await _users.RegisterUser(id, message.From.Username, ct);
            await Reply(id, "Добро пожаловать в ToDo бот! Справка: /help.", ct);
            return;
        }
        if (command == "/help")
        {
            await Reply(id, "/start - регистрация\n/addtask - название, срок dd.MM.yyyy и выбор списка\n/show - выбор списка и просмотр активных задач\n/report - статистика\n/cancel - отмена сценария\n/completetask <id> - завершить задачу\n/removetask <id> - удалить задачу\n/help - справка\nСписки создаются и удаляются кнопками в /show. Название списка - от 1 до 10 символов, уникально у пользователя. Удаление списка и всех его задач требует подтверждения.", ct);
            return;
        }
        var user = await _users.GetUser(id, ct);
        if (user is null) { await Reply(id, "Сначала зарегистрируйтесь командой /start.", ct); return; }
        switch (command)
        {
            case "/addtask": await ProcessScenario(new(ScenarioType.AddTask) { UserId = id }, update, ct); break;
            case "/show":
                await _bot.SendMessage(id, "Выберите список", replyMarkup: BotKeyboards.Lists(
                    await _lists.GetUserLists(user.UserId, ct), "show", management: true), cancellationToken: ct);
                break;
            case "/report":
                var tasks = await _tasks.GetAllByUserId(user.UserId, ct);
                await Reply(id, $"Всего: {tasks.Count}\nАктивных: {tasks.Count(x => x.State == ToDoItemState.Active)}\nЗавершённых: {tasks.Count(x => x.State == ToDoItemState.Completed)}", ct);
                break;
            case "/completetask":
            case "/removetask":
                if (!Guid.TryParse(argument, out var taskId)) { await Reply(id, $"Формат: {command} <id>. ID задачи можно получить через /show.", ct); break; }
                var changed = command == "/completetask" ? await _tasks.Complete(user.UserId, taskId, ct) : await _tasks.Delete(user.UserId, taskId, ct);
                await Reply(id, !changed ? "Задача не найдена." : command == "/completetask" ? "Задача завершена." : "Задача удалена.", ct);
                break;
            default: await Reply(id, "Выберите команду на клавиатуре или откройте /help.", ct); break;
        }
    }

    private async Task OnCallbackQuery(Update update, CallbackQuery query, CancellationToken ct)
    {
        var user = await _users.GetUser(query.From.Id, ct);
        if (user is null)
        {
            await _bot.AnswerCallbackQuery(query.Id, "Сначала зарегистрируйтесь командой /start.", cancellationToken: ct);
            return;
        }
        await _bot.AnswerCallbackQuery(query.Id, cancellationToken: ct);
        try
        {
            var dto = CallbackDto.FromString(query.Data ?? "");
            var context = await _contexts.GetContext(query.From.Id, ct);
            if (context is not null) { await ProcessScenario(context, update, ct); return; }
            switch (dto.Action)
            {
                case "show":
                    var choice = ToDoListCallbackDto.FromString(query.Data!);
                    if (choice.ToDoListId is Guid listId)
                    {
                        var list = await _lists.Get(listId, ct);
                        if (list is null || list.User.UserId != user.UserId)
                        { await Reply(query.From.Id, "Список не найден или недоступен. Откройте /show.", ct); return; }
                    }
                    var tasks = (await _tasks.GetByUserIdAndList(user.UserId, choice.ToDoListId, ct))
                        .Where(x => x.State == ToDoItemState.Active).ToArray();
                    if (tasks.Length == 0) { await Reply(query.From.Id, "Активных задач нет.", ct); return; }
                    foreach (var item in tasks)
                        await Reply(query.From.Id, $"{item.Name}\nСрок: {item.Deadline.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}\nСписок: {item.List?.Name ?? "Без списка"}\nID: {item.Id}", ct);
                    break;
                case "addlist": await ProcessScenario(new(ScenarioType.AddList) { UserId = query.From.Id }, update, ct); break;
                case "deletelist":
                    if (query.Data != "deletelist") { await Reply(query.From.Id, "Эта кнопка устарела. Откройте /show.", ct); return; }
                    await ProcessScenario(new(ScenarioType.DeleteList) { UserId = query.From.Id }, update, ct);
                    break;
                default: await Reply(query.From.Id, "Эта кнопка устарела. Откройте /show.", ct); break;
            }
        }
        catch (FormatException)
        {
            var active = await _contexts.GetContext(query.From.Id, ct);
            await _bot.SendMessage(query.From.Id,
                active is null
                    ? "Некорректные данные кнопки. Откройте /show."
                    : "Некорректные данные кнопки. Используйте последнюю клавиатуру или /cancel.",
                replyMarkup: active is null ? BotKeyboards.Main : BotKeyboards.Cancel,
                cancellationToken: ct);
        }
    }

    public IScenario GetScenario(ScenarioType scenario) => _scenarios.FirstOrDefault(x => x.CanHandle(scenario))
        ?? throw new InvalidOperationException($"Обработчик сценария {scenario} не найден.");
    public async Task ProcessScenario(ScenarioContext context, Update update, CancellationToken ct)
    {
        var result = await GetScenario(context.CurrentScenario).HandleMessageAsync(_bot, context, update, ct);
        if (result == ScenarioResult.Completed)
        {
            await _contexts.ResetContext(context.UserId, ct);
            await Reply(context.UserId, "Выберите следующую команду:", ct);
        }
        else await _contexts.SetContext(context.UserId, context, ct);
    }
    private Task Reply(long chat, string text, CancellationToken ct) =>
        _bot.SendMessage(chat, text, replyMarkup: BotKeyboards.Main, cancellationToken: ct);
}
