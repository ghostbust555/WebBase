using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Azure.Cosmos;
using User = WebBase.Models.User;

namespace WebBase.Database.CosmosDB;

public class CosmosUserStore(CosmosClient client, CosmosSettings settings) : IUserStore
{
    private Container Users => client.GetContainer(settings.DatabaseName, CosmosSettings.Users);
    private Container Keys => client.GetContainer(settings.DatabaseName, CosmosSettings.Keys);

    /// <summary>"This unique value belongs to that user."</summary>
    private record KeyDoc(string Id, Guid UserId, DateTime CreatedAtUtc);

    // Ids can't contain / \ ? #, which emails may: store a hash instead.
    private static string KeyId(string kind, string value) =>
        $"{kind}-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()}";

    private static IEnumerable<string> KeysOf(User u)
    {
        yield return KeyId("email", u.Email);
        if (u.GoogleSubject != null) yield return KeyId("google", u.GoogleSubject);
    }

    public async Task<User?> FindByIdAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            return (await Users.ReadItemAsync<User>(id.ToString(), new PartitionKey(id.ToString()), cancellationToken: ct)).Resource;
        }
        catch (CosmosException e) when (e.Is(HttpStatusCode.NotFound))
        {
            return null;
        }
    }

    public Task<User?> FindByEmailAsync(string email, CancellationToken ct = default) => FindByKeyAsync(KeyId("email", email), ct);

    public Task<User?> FindByGoogleSubjectAsync(string subject, CancellationToken ct = default) => FindByKeyAsync(KeyId("google", subject), ct);

    private async Task<User?> FindByKeyAsync(string keyId, CancellationToken ct)
    {
        var key = await ReadKeyAsync(keyId, ct);
        if (key == null) return null;
        var user = await FindByIdAsync(key.UserId, ct);
        // A key left over from an old value that hasn't been cleaned up yet.
        return user != null && KeysOf(user).Contains(keyId) ? user : null;
    }

    private async Task<KeyDoc?> ReadKeyAsync(string keyId, CancellationToken ct)
    {
        try
        {
            return (await Keys.ReadItemAsync<KeyDoc>(keyId, new PartitionKey(keyId), cancellationToken: ct)).Resource;
        }
        catch (CosmosException e) when (e.Is(HttpStatusCode.NotFound))
        {
            return null;
        }
    }

    /// <summary>
    /// Take a unique value for a user. Creating a document whose id exists is
    /// refused by Cosmos (409), and that refusal is the uniqueness rule. A key
    /// whose owner no longer uses it (a crash halfway through, an old email)
    /// may be taken over, but only once it's a minute old - a newer one may
    /// belong to a registration still in progress.
    /// </summary>
    private async Task ClaimAsync(string keyId, Guid userId, CancellationToken ct)
    {
        try
        {
            await Keys.CreateItemAsync(new KeyDoc(keyId, userId, DateTime.UtcNow), new PartitionKey(keyId), cancellationToken: ct);
            return;
        }
        catch (CosmosException e) when (e.Is(HttpStatusCode.Conflict)) { }

        var existing = await Keys.ReadItemAsync<KeyDoc>(keyId, new PartitionKey(keyId), cancellationToken: ct);
        if (existing.Resource.UserId == userId) return;
        var owner = await FindByIdAsync(existing.Resource.UserId, ct);
        var abandoned = (owner == null || !KeysOf(owner).Contains(keyId))
                        && existing.Resource.CreatedAtUtc < DateTime.UtcNow.AddMinutes(-1);
        if (!abandoned) throw new DuplicateKeyException("That value already belongs to another user.");
        try
        {
            await Keys.ReplaceItemAsync(new KeyDoc(keyId, userId, DateTime.UtcNow), keyId, new PartitionKey(keyId),
                new ItemRequestOptions { IfMatchEtag = existing.ETag }, ct);
        }
        catch (CosmosException e) when (e.Is(HttpStatusCode.PreconditionFailed))
        {
            throw new DuplicateKeyException("That value was just taken by another user.");
        }
    }

    private async Task ReleaseAsync(IEnumerable<string> keyIds, CancellationToken ct)
    {
        foreach (var keyId in keyIds)
        {
            try { await Keys.DeleteItemAsync<KeyDoc>(keyId, new PartitionKey(keyId), cancellationToken: ct); }
            catch (CosmosException e) when (e.Is(HttpStatusCode.NotFound)) { }
        }
    }

    public async Task AddAsync(User user, CancellationToken ct = default)
    {
        var claimed = new List<string>();
        try
        {
            foreach (var key in KeysOf(user))
            {
                await ClaimAsync(key, user.Id, ct);
                claimed.Add(key);
            }
            await Users.CreateItemAsync(user, new PartitionKey(user.Id.ToString()), cancellationToken: ct);
        }
        catch (CosmosException e) when (e.Is(HttpStatusCode.Conflict))
        {
            await ReleaseAsync(claimed, CancellationToken.None);
            throw new DuplicateKeyException("A user with that id already exists.");
        }
        catch
        {
            await ReleaseAsync(claimed, CancellationToken.None);
            throw;
        }
    }

    public async Task UpdateAsync(User user, CancellationToken ct = default)
    {
        var old = await FindByIdAsync(user.Id, ct) ?? throw new KeyNotFoundException("That user doesn't exist.");
        var oldKeys = KeysOf(old).ToList();
        var added = KeysOf(user).Except(oldKeys).ToList();
        var claimed = new List<string>();
        try
        {
            foreach (var key in added)
            {
                await ClaimAsync(key, user.Id, ct);
                claimed.Add(key);
            }
            await Users.ReplaceItemAsync(user, user.Id.ToString(), new PartitionKey(user.Id.ToString()), cancellationToken: ct);
        }
        catch
        {
            await ReleaseAsync(claimed, CancellationToken.None);
            throw;
        }
        await ReleaseAsync(oldKeys.Except(KeysOf(user)), ct);
    }
}
