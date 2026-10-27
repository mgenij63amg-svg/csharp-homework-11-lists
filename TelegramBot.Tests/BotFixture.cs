using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Core.Entities;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramBot.Dto;
using TelegramBot.Repositories;
using TelegramBot.Scenarios;
using TelegramBot.Services;

namespace TelegramBot.Tests;

internal sealed record ApiCall(string Method, JsonElement Payload, int MessageId);
internal sealed class RecordingTransport : HttpMessageHandler
{
    private readonly ConcurrentQueue<ApiCall> _calls = new();
    private int _id;
    public bool FailNextSend { get; set; }
    public ApiCall[] Calls => _calls.ToArray();
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var method = request.RequestUri!.AbsolutePath.Split('/').Last();
        using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
        var payload = doc.RootElement.Clone();
        if (method == "sendMessage" && FailNextSend)
        { FailNextSend = false; throw new HttpRequestException("Test transport failure"); }
        var id = Interlocked.Increment(ref _id);
        _calls.Enqueue(new(method, payload, method == "sendMessage" ? id : 0));
        object result = method switch
        {
            "answerCallbackQuery" => true,
            "sendMessage" => new
            {
                message_id = id, date = 1700000000,
                chat = new { id = ReadChat(payload), type = "private" },
                text = payload.GetProperty("text").GetString()
            },
            _ => throw new InvalidOperationException("Unexpected API method in test: " + method)
        };
        return new(HttpStatusCode.OK)
        { Content = new StringContent(JsonSerializer.Serialize(new { ok = true, result }), Encoding.UTF8, "application/json") };
    }
    public static long ReadChat(JsonElement payload) => payload.GetProperty("chat_id").ValueKind == JsonValueKind.String
        ? long.Parse(payload.GetProperty("chat_id").GetString()!) : payload.GetProperty("chat_id").GetInt64();
}

internal sealed class BotFixture : IDisposable
{
    private static readonly string TestRoot = Path.Combine(Path.GetTempPath(), "ToDoBot.Tests");
    public string DirectoryPath { get; } = Path.Combine(TestRoot, Guid.NewGuid().ToString("N"));
    public RecordingTransport Transport { get; } = new();
    private readonly HttpClient _http;
    public TelegramBotClient Bot { get; }
    public UserService Users { get; }
    public ToDoService Tasks { get; }
    public ToDoListService Lists { get; }
    public InMemoryScenarioContextRepository Contexts { get; } = new();
    public UpdateHandler Handler { get; }
    public BotFixture()
    {
        _http = new(Transport);
        // HTTP перехвачен целиком; настоящее значение ключа не используется.
        Bot = new TelegramBotClient("123456789:" + new string('x', 35), _http);
        Users = new(new FileUserRepository(DirectoryPath));
        Lists = new(new FileToDoListRepository(DirectoryPath));
        Tasks = new(DirectoryPath, Lists);
        Handler = new(Bot, Users, Tasks, new IScenario[]
        { new AddTaskScenario(Users, Tasks, Lists), new AddListScenario(Users, Lists), new DeleteListScenario(Users, Lists, Tasks) }, Contexts, Lists);
    }
    public static Update MessageUpdate(string? text, long user = 1, ChatType type = ChatType.Private) => new()
    {
        Id = 1, Message = new Message { Id = 1, Date = DateTime.UtcNow,
            From = new User { Id = user, FirstName = "Test" }, Chat = new Chat { Id = user, Type = type }, Text = text }
    };
    public Task Send(string? text, long user = 1, CancellationToken ct = default) =>
        Handler.HandleUpdateAsync(Bot, MessageUpdate(text, user), ct);
    public async Task Click(string? data, long user = 1, int? messageId = null)
    {
        var context = await Context(user);
        var id = messageId ?? (context?.Data.TryGetValue("PromptId", out var saved) == true
            ? (int)saved : LastSend(user).MessageId);
        await Handler.HandleUpdateAsync(Bot, new Update
        {
            Id = 2, CallbackQuery = new CallbackQuery
            {
                Id = Guid.NewGuid().ToString("N"), ChatInstance = "test", Data = data,
                From = new User { Id = user, FirstName = "Test" },
                Message = new Message { Id = id, Date = DateTime.UtcNow, Chat = new Chat { Id = user, Type = ChatType.Private } }
            }
        }, default);
    }
    public Task Choose(Guid? id, long user = 1, int? messageId = null) =>
        Click(new ToDoListCallbackDto { Action = "tasklist", ToDoListId = id }.ToString(), user, messageId);
    public Task<ScenarioContext?> Context(long user = 1) => Contexts.GetContext(user, default);
    public async Task Begin(long user = 1) { await Send("/start", user); await Send("/addtask", user); }
    public async Task CreateTask(string name = "Task", Guid? listId = null, long user = 1)
    { await Begin(user); await Send(name, user); await Send("25.05.2025", user); await Choose(listId, user); }
    public async Task<ToDoList> CreateList(string name = "Test", long user = 1)
    {
        await Send("/start", user); await Send("/show", user); await Click("addlist", user); await Send(name, user);
        return (await Lists.GetUserLists((await Users.GetUser(user, default))!.UserId, default)).Single(x => x.Name == name);
    }
    public async Task<IReadOnlyList<ToDoItem>> Items(long user = 1)
    {
        var owner = await Users.GetUser(user, default);
        return owner is null ? Array.Empty<ToDoItem>() : await Tasks.GetAllByUserId(owner.UserId, default);
    }
    public ApiCall LastSend(long user = 1) => Transport.Calls.Last(x => x.Method == "sendMessage" && RecordingTransport.ReadChat(x.Payload) == user);
    public string LastText => LastSend().Payload.GetProperty("text").GetString()!;
    public string[] LastButtons => LastSend().Payload.GetProperty("reply_markup").GetProperty("keyboard")
        .EnumerateArray().SelectMany(x => x.EnumerateArray()).Select(x => x.GetProperty("text").GetString()!).ToArray();
    public string[] LastInline => LastSend().Payload.GetProperty("reply_markup").GetProperty("inline_keyboard")
        .EnumerateArray().SelectMany(x => x.EnumerateArray()).Select(x => x.GetProperty("callback_data").GetString()!).ToArray();
    public string AllText => string.Join("\n", Transport.Calls.Where(x => x.Method == "sendMessage")
        .Select(x => x.Payload.GetProperty("text").GetString()));
    public void Dispose()
    {
        _http.Dispose();
        var resolved = Path.GetFullPath(DirectoryPath);
        if (!resolved.StartsWith(Path.GetFullPath(TestRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid test directory");
        if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
    }
}
