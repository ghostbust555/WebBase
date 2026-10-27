using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WebBase.Data;
using WebBase.Models;
using WebBase.Modules;

namespace WebBase.Testing.InMemory;

/// <summary>
/// A database that lives in memory, for the API tests: fast, nothing to
/// start, and it keeps the same contract as the real ones (InMemoryStoreTests
/// runs StoreContractTests against it), uniqueness included. Never used by
/// the app itself.
/// </summary>
public class InMemoryDatabaseModule : IDatabaseModule
{
    public string Name => "memory";

    public void Register(IServiceCollection services, IConfiguration config) => Register(services);

    /// <summary>
    /// Replace whatever database is registered with a fresh in-memory one:
    /// every IDatabase/store registration (anything from WebBase.Data) goes,
    /// so a new store needs an in-memory version here too.
    /// </summary>
    public static void Register(IServiceCollection services)
    {
        foreach (var d in services.Where(d => d.ServiceType.Namespace == typeof(IDatabase).Namespace).ToList())
            services.Remove(d);
        var data = new InMemoryData();
        services.AddSingleton(data);
        services.AddSingleton<IDatabase, InMemoryDatabase>();
        services.AddSingleton<IUserStore, InMemoryUserStore>();
        services.AddSingleton<INoteStore, InMemoryNoteStore>();
        services.AddSingleton<IPageStore, InMemoryPageStore>();
    }
}

/// <summary>Everything stored, behind one lock.</summary>
public class InMemoryData
{
    public readonly Lock Lock = new();
    public readonly Dictionary<Guid, User> Users = [];
    public readonly List<Note> Notes = [];
    public readonly Dictionary<Guid, Page> Pages = [];

    private static readonly MethodInfo Clone = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!;

    /// <summary>Reads return copies (the contract): the models are flat, so a shallow copy is a full one.</summary>
    public static T Copy<T>(T item) where T : class => (T)Clone.Invoke(item, null)!;
}

public class InMemoryDatabase : IDatabase
{
    public string Provider => "memory";
    public Task PrepareAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task<bool> CanConnectAsync(CancellationToken ct = default) => Task.FromResult(true);
}

public class InMemoryUserStore(InMemoryData data) : IUserStore
{
    public Task<User?> FindByIdAsync(Guid id, CancellationToken ct = default) =>
        Find(u => u.Id == id);

    public Task<User?> FindByEmailAsync(string email, CancellationToken ct = default) =>
        Find(u => u.Email == email);

    public Task<User?> FindByGoogleSubjectAsync(string subject, CancellationToken ct = default) =>
        Find(u => u.GoogleSubject == subject);

    public Task<bool> AnyAsync(CancellationToken ct = default)
    {
        lock (data.Lock) return Task.FromResult(data.Users.Count > 0);
    }

    public Task<IReadOnlyList<User>> ListAuthorsAsync(CancellationToken ct = default)
    {
        lock (data.Lock)
            return Task.FromResult<IReadOnlyList<User>>(data.Users.Values.Where(u => u.IsAuthor)
                .OrderBy(u => u.CreatedAtUtc).Select(InMemoryData.Copy).ToList());
    }

    public Task AddAsync(User user, CancellationToken ct = default)
    {
        lock (data.Lock)
        {
            if (data.Users.ContainsKey(user.Id)) throw new DuplicateKeyException("That user already exists.");
            CheckUnique(user);
            data.Users[user.Id] = InMemoryData.Copy(user);
        }
        return Task.CompletedTask;
    }

    public Task UpdateAsync(User user, CancellationToken ct = default)
    {
        lock (data.Lock)
        {
            if (!data.Users.ContainsKey(user.Id)) throw new KeyNotFoundException($"No user {user.Id}.");
            CheckUnique(user);
            data.Users[user.Id] = InMemoryData.Copy(user);
        }
        return Task.CompletedTask;
    }

    private void CheckUnique(User user)
    {
        foreach (var other in data.Users.Values.Where(u => u.Id != user.Id))
        {
            if (other.Email == user.Email) throw new DuplicateKeyException("That email is taken.");
            if (user.GoogleSubject != null && other.GoogleSubject == user.GoogleSubject)
                throw new DuplicateKeyException("That Google account is taken.");
        }
    }

    private Task<User?> Find(Func<User, bool> match)
    {
        lock (data.Lock)
            return Task.FromResult(data.Users.Values.Where(match).Select(InMemoryData.Copy).FirstOrDefault());
    }
}

public class InMemoryNoteStore(InMemoryData data) : INoteStore
{
    public Task<IReadOnlyList<Note>> ListAsync(Guid userId, CancellationToken ct = default)
    {
        lock (data.Lock)
            return Task.FromResult<IReadOnlyList<Note>>(data.Notes.Where(n => n.UserId == userId)
                .OrderByDescending(n => n.CreatedAtUtc).Select(InMemoryData.Copy).ToList());
    }

    public Task AddAsync(Note note, CancellationToken ct = default)
    {
        lock (data.Lock) data.Notes.Add(InMemoryData.Copy(note));
        return Task.CompletedTask;
    }

    public Task<bool> RemoveAsync(Guid userId, Guid noteId, CancellationToken ct = default)
    {
        lock (data.Lock)
            return Task.FromResult(data.Notes.RemoveAll(n => n.UserId == userId && n.Id == noteId) > 0);
    }
}

public class InMemoryPageStore(InMemoryData data) : IPageStore
{
    public Task<IReadOnlyList<Page>> ListAsync(CancellationToken ct = default)
    {
        lock (data.Lock)
            return Task.FromResult<IReadOnlyList<Page>>(data.Pages.Values.Select(InMemoryData.Copy).ToList());
    }

    public Task<IReadOnlyList<Page>> ListPublishedAsync(CancellationToken ct = default)
    {
        lock (data.Lock)
            return Task.FromResult<IReadOnlyList<Page>>(data.Pages.Values.Where(p => p.PublishedAtUtc != null)
                .Select(InMemoryData.Copy).ToList());
    }

    public Task<Page?> FindByIdAsync(Guid id, CancellationToken ct = default)
    {
        lock (data.Lock)
            return Task.FromResult(data.Pages.TryGetValue(id, out var page) ? InMemoryData.Copy(page) : null);
    }

    public Task<Page?> FindByPathAsync(string path, CancellationToken ct = default)
    {
        lock (data.Lock)
            return Task.FromResult(data.Pages.Values.Where(p => p.Path == path).Select(InMemoryData.Copy).FirstOrDefault());
    }

    public Task AddAsync(Page page, CancellationToken ct = default)
    {
        lock (data.Lock)
        {
            if (data.Pages.ContainsKey(page.Id)) throw new DuplicateKeyException("That page already exists.");
            CheckUnique(page);
            data.Pages[page.Id] = InMemoryData.Copy(page);
        }
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Page page, CancellationToken ct = default)
    {
        lock (data.Lock)
        {
            if (!data.Pages.ContainsKey(page.Id)) throw new KeyNotFoundException($"No page {page.Id}.");
            CheckUnique(page);
            data.Pages[page.Id] = InMemoryData.Copy(page);
        }
        return Task.CompletedTask;
    }

    public Task<bool> RemoveAsync(Guid id, CancellationToken ct = default)
    {
        lock (data.Lock) return Task.FromResult(data.Pages.Remove(id));
    }

    private void CheckUnique(Page page)
    {
        if (data.Pages.Values.Any(p => p.Id != page.Id && p.Path == page.Path))
            throw new DuplicateKeyException("That path is taken.");
    }
}
