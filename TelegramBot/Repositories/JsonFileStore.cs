using System.Collections.Concurrent;
using System.Text.Json;

namespace TelegramBot.Repositories;

internal sealed class JsonFileStore<T>
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _path;
    private readonly SemaphoreSlim _gate;

    public JsonFileStore(string directory, string fileName)
    {
        _path = Path.GetFullPath(Path.Combine(directory, fileName));
        _gate = Gates.GetOrAdd(_path, _ => new SemaphoreSlim(1, 1));
    }

    private async Task<List<T>> Load(CancellationToken ct)
    {
        if (!File.Exists(_path)) return new();
        try
        {
            return JsonSerializer.Deserialize<List<T>>(await File.ReadAllTextAsync(_path, ct), Options)
                ?? throw new InvalidDataException($"Пустое содержимое хранилища {Path.GetFileName(_path)}.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Повреждено хранилище {Path.GetFileName(_path)}.", ex);
        }
    }

    public async Task<IReadOnlyList<T>> Read(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { return (await Load(ct)).ToArray(); }
        finally { _gate.Release(); }
    }

    public async Task<TResult> Change<TResult>(Func<List<T>, TResult> change, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        string? temporary = null;
        try
        {
            var values = await Load(ct);
            var result = change(values);
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(values, Options), ct);
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, _path, overwrite: true);
            return result;
        }
        finally
        {
            if (temporary is not null && File.Exists(temporary)) File.Delete(temporary);
            _gate.Release();
        }
    }
}
