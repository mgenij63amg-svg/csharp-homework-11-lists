using Telegram.Bot.Types;
namespace TelegramBot.Scenarios;

internal static class ScenarioUi
{
    public static long ChatId(Update update) => update.Message?.Chat.Id ?? update.CallbackQuery?.Message?.Chat.Id
        ?? throw new ArgumentException("Отсутствует чат.");
    public static void Remember(ScenarioContext context, Message prompt) => context.Data["PromptId"] = prompt.Id;
    public static bool IsCurrent(ScenarioContext context, Update update) =>
        update.CallbackQuery?.Message?.Id is int id && context.Data.TryGetValue("PromptId", out var saved) && id == (int)saved;
}
