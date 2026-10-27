using Google.Cloud.Firestore;
using static WebBase.Database.Firestore.FirestoreValues;

namespace WebBase.Database.Firestore;

/// <summary>
/// IPageStore on Firestore. A page's address is kept unique by a "keys"
/// document (path-...), written in the same transaction as the page.
/// </summary>
public class FirestorePageStore(FirestoreDb db) : IPageStore
{
    private CollectionReference Pages => db.Collection(FirestoreNames.Pages);
    private DocumentReference PathKey(string path) => db.Collection(FirestoreNames.Keys).Document(KeyId("path", path));

    private static Dictionary<string, object?> ToDoc(Page p) => new()
    {
        ["path"] = p.Path,
        ["title"] = p.Title,
        ["body"] = p.Body,
        ["showInMenu"] = p.ShowInMenu,
        ["order"] = p.Order,
        ["publishedAtUtc"] = Time(p.PublishedAtUtc),
        ["published"] = p.PublishedAtUtc != null, // what ListPublishedAsync asks for
        ["createdAtUtc"] = Time(p.CreatedAtUtc),
        ["updatedAtUtc"] = Time(p.UpdatedAtUtc),
        ["updatedById"] = p.UpdatedById.ToString(),
    };

    private static Page ToPage(DocumentSnapshot d) => new()
    {
        Id = Guid.Parse(d.Id),
        Path = d.GetValue<string>("path"),
        Title = d.GetValue<string>("title"),
        Body = d.GetValue<string>("body"),
        ShowInMenu = d.GetValue<bool>("showInMenu"),
        Order = d.GetValue<int>("order"),
        PublishedAtUtc = OptionalTimeOf(d, "publishedAtUtc"),
        CreatedAtUtc = TimeOf(d, "createdAtUtc"),
        UpdatedAtUtc = TimeOf(d, "updatedAtUtc"),
        UpdatedById = Guid.Parse(d.GetValue<string>("updatedById")),
    };

    public async Task<IReadOnlyList<Page>> ListAsync(CancellationToken ct = default) =>
        (await Pages.GetSnapshotAsync(ct)).Select(ToPage).ToList();

    public async Task<IReadOnlyList<Page>> ListPublishedAsync(CancellationToken ct = default) =>
        (await Pages.WhereEqualTo("published", true).GetSnapshotAsync(ct)).Select(ToPage).ToList();

    public async Task<Page?> FindByIdAsync(Guid id, CancellationToken ct = default)
    {
        var doc = await Pages.Document(id.ToString()).GetSnapshotAsync(ct);
        return doc.Exists ? ToPage(doc) : null;
    }

    public async Task<Page?> FindByPathAsync(string path, CancellationToken ct = default)
    {
        var found = await Pages.WhereEqualTo("path", path).Limit(1).GetSnapshotAsync(ct);
        return found.Count == 0 ? null : ToPage(found[0]);
    }

    public Task AddAsync(Page page, CancellationToken ct = default) =>
        db.RunTransactionAsync(async tx =>
        {
            var pageRef = Pages.Document(page.Id.ToString());
            if ((await tx.GetSnapshotAsync(pageRef, ct)).Exists) throw new DuplicateKeyException("A page with that id already exists.");
            if ((await tx.GetSnapshotAsync(PathKey(page.Path), ct)).Exists) throw new DuplicateKeyException("Another page already has that path.");
            tx.Create(pageRef, ToDoc(page));
            tx.Create(PathKey(page.Path), KeyDoc(page.Id.ToString()));
        }, cancellationToken: ct);

    public Task UpdateAsync(Page page, CancellationToken ct = default) =>
        db.RunTransactionAsync(async tx =>
        {
            var id = page.Id.ToString();
            var pageRef = Pages.Document(id);
            var old = await tx.GetSnapshotAsync(pageRef, ct);
            if (!old.Exists) throw new KeyNotFoundException($"No page {page.Id}.");
            var oldPath = old.GetValue<string>("path");
            if (oldPath != page.Path)
            {
                var claim = await tx.GetSnapshotAsync(PathKey(page.Path), ct);
                if (claim.Exists && claim.GetValue<string>("owner") != id) throw new DuplicateKeyException("Another page already has that path.");
                tx.Delete(PathKey(oldPath));
                tx.Set(PathKey(page.Path), KeyDoc(id));
            }
            tx.Set(pageRef, ToDoc(page));
        }, cancellationToken: ct);

    public Task<bool> RemoveAsync(Guid id, CancellationToken ct = default) =>
        db.RunTransactionAsync(async tx =>
        {
            var pageRef = Pages.Document(id.ToString());
            var old = await tx.GetSnapshotAsync(pageRef, ct);
            if (!old.Exists) return false;
            tx.Delete(pageRef);
            tx.Delete(PathKey(old.GetValue<string>("path")));
            return true;
        }, cancellationToken: ct);
}
