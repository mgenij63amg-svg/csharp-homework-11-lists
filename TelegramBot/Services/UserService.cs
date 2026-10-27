using Core.Entities;
using TelegramBot.Repositories;
namespace TelegramBot.Services;

public sealed class UserService : IUserService
{
    private readonly FileUserRepository _repository;
    public UserService(FileUserRepository repository) => _repository = repository;
    public Task<ToDoUser> RegisterUser(long telegramUserId, string? userName, CancellationToken ct) =>
        _repository.Register(telegramUserId, userName, ct);
    public Task<ToDoUser?> GetUser(long telegramUserId, CancellationToken ct) => _repository.Get(telegramUserId, ct);
}
