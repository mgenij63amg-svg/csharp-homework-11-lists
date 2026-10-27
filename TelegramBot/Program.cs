using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;
using TelegramBot;
using TelegramBot.Repositories;
using TelegramBot.Scenarios;
using TelegramBot.Services;

Console.OutputEncoding = System.Text.Encoding.UTF8;
var token = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
if (string.IsNullOrWhiteSpace(token))
{
    Console.Error.WriteLine("Задайте переменную окружения TELEGRAM_BOT_TOKEN с токеном от @BotFather.");
    return 1;
}
var directory = Environment.GetEnvironmentVariable("TELEGRAM_DATA_DIR") ?? Path.Combine(Environment.CurrentDirectory, "data");
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, args) => { args.Cancel = true; cts.Cancel(); };
try
{
    var bot = new TelegramBotClient(token);
    var users = new UserService(new FileUserRepository(directory));
    var lists = new ToDoListService(new FileToDoListRepository(directory));
    var tasks = new ToDoService(directory, lists);
    var contexts = new InMemoryScenarioContextRepository();
    IScenario[] scenarios = { new AddTaskScenario(users, tasks, lists), new AddListScenario(users, lists), new DeleteListScenario(users, lists, tasks) };
    var handler = new UpdateHandler(bot, users, tasks, scenarios, contexts, lists);
    var me = await bot.GetMe(cts.Token);
    Console.WriteLine($"Бот @{me.Username} запущен. Остановка: Ctrl+C.");
    await bot.ReceiveAsync(handler.HandleUpdateAsync,
        async (_, _, ct) =>
        {
            Console.Error.WriteLine("Не удалось обработать обновление. Повтор через 3 секунды.");
            await Task.Delay(TimeSpan.FromSeconds(3), ct);
        }, new ReceiverOptions { AllowedUpdates = new[] { UpdateType.Message, UpdateType.CallbackQuery } }, cts.Token);
}
catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
catch (Exception ex)
{
    Console.Error.WriteLine($"Запуск не выполнен ({ex.GetType().Name}). Проверьте доступ к Telegram и настройку токена.");
    return 1;
}
return 0;
