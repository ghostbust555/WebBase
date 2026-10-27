using System.Net;
using Microsoft.Azure.Cosmos;

namespace WebBase.Database.CosmosDB;

/// <summary>
/// IPageStore on Cosmos DB. The pages are site-owned and few, so they all share
/// ONE logical partition (the constant "site"): that is what lets the container's
/// unique key on /path mean "unique in the whole site" (Cosmos enforces unique
/// keys within a partition only), and lists are cheap single-partition reads.
/// </summary>
public class CosmosPageStore(CosmosClient client, CosmosSettings settings) : IPageStore
{
    private Container Pages => client.GetContainer(settings.DatabaseName, CosmosSettings.Pages);
    private static readonly PartitionKey Site = new(PageDoc.SiteValue);

    /// <summary>The stored shape: the model plus the constant partition key.</summary>
    private record PageDoc(string Id, string Site, string Path, string Title, string Body, bool ShowInMenu, int Order,
        DateTime? PublishedAtUtc, DateTime CreatedAtUtc, DateTime UpdatedAtUtc, Guid UpdatedById)
    {
        public const string SiteValue = "site";

        public static PageDoc From(Page p) => new(p.Id.ToString(), SiteValue, p.Path, p.Title, p.Body, p.ShowInMenu, p.Order,
            p.PublishedAtUtc, p.CreatedAtUtc, p.UpdatedAtUtc, p.UpdatedById);

        public Page ToPage() => new()
        {
            Id = Guid.Parse(Id), Path = Path, Title = Title, Body = Body, ShowInMenu = ShowInMenu, Order = Order,
            PublishedAtUtc = PublishedAtUtc, CreatedAtUtc = CreatedAtUtc, UpdatedAtUtc = UpdatedAtUtc, UpdatedById = UpdatedById,
        };
    }

    private async Task<List<Page>> QueryAsync(QueryDefinition query, CancellationToken ct)
    {
        var found = new List<Page>();
        using var feed = Pages.GetItemQueryIterator<PageDoc>(query, requestOptions: new QueryRequestOptions { PartitionKey = Site });
        while (feed.HasMoreResults) found.AddRange((await feed.ReadNextAsync(ct)).Select(d => d.ToPage()));
        return found;
    }

    public async Task<IReadOnlyList<Page>> ListAsync(CancellationToken ct = default) =>
        await QueryAsync(new QueryDefinition("SELECT * FROM c"), ct);

    public async Task<IReadOnlyList<Page>> ListPublishedAsync(CancellationToken ct = default) =>
        await QueryAsync(new QueryDefinition("SELECT * FROM c WHERE IS_DEFINED(c.publishedAtUtc) AND NOT IS_NULL(c.publishedAtUtc)"), ct);

    public async Task<Page?> FindByIdAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            return (await Pages.ReadItemAsync<PageDoc>(id.ToString(), Site, cancellationToken: ct)).Resource.ToPage();
        }
        catch (CosmosException e) when (e.Is(HttpStatusCode.NotFound))
        {
            return null;
        }
    }

    public async Task<Page?> FindByPathAsync(string path, CancellationToken ct = default) =>
        (await QueryAsync(new QueryDefinition("SELECT * FROM c WHERE c.path = @path").WithParameter("@path", path), ct)).FirstOrDefault();

    public async Task AddAsync(Page page, CancellationToken ct = default)
    {
        try
        {
            await Pages.CreateItemAsync(PageDoc.From(page), Site, cancellationToken: ct);
        }
        catch (CosmosException e) when (e.Is(HttpStatusCode.Conflict))
        {
            // Either the id or the path (the container's unique key) already exists.
            throw new DuplicateKeyException("That page, or a page at that path, already exists.");
        }
    }

    public async Task UpdateAsync(Page page, CancellationToken ct = default)
    {
        try
        {
            await Pages.ReplaceItemAsync(PageDoc.From(page), page.Id.ToString(), Site, cancellationToken: ct);
        }
        catch (CosmosException e) when (e.Is(HttpStatusCode.NotFound))
        {
            throw new KeyNotFoundException($"No page {page.Id}.");
        }
        catch (CosmosException e) when (e.Is(HttpStatusCode.Conflict))
        {
            throw new DuplicateKeyException("Another page already has that path.");
        }
    }

    public async Task<bool> RemoveAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            await Pages.DeleteItemAsync<PageDoc>(id.ToString(), Site, cancellationToken: ct);
            return true;
        }
        catch (CosmosException e) when (e.Is(HttpStatusCode.NotFound))
        {
            return false;
        }
    }
}
