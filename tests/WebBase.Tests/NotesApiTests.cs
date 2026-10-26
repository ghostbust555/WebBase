using System.Net;
using System.Net.Http.Json;
using WebBase.Api.Contracts;

namespace WebBase.Tests;

/// <summary>
/// Tests for the example feature. The isolation test is the one to copy for
/// every feature you add: one user must never see or touch another's rows.
/// </summary>
public class NotesApiTests(TestApp app) : IClassFixture<TestApp>
{
    [Fact]
    public async Task A_note_can_be_added_listed_and_deleted()
    {
        var client = await app.SignedInClientAsync();

        var created = await (await client.PostAsJsonAsync("/api/notes", new CreateNoteRequest("  buy milk  ")))
            .Content.ReadFromJsonAsync<NoteDto>();
        Assert.Equal("buy milk", created!.Text);

        var list = await client.GetFromJsonAsync<List<NoteDto>>("/api/notes");
        Assert.Single(list!);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/notes/{created.Id}")).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<List<NoteDto>>("/api/notes"))!);
    }

    [Fact]
    public async Task One_user_cannot_see_or_delete_another_users_notes()
    {
        var alice = await app.SignedInClientAsync();
        var bob = await app.SignedInClientAsync();
        var note = await (await alice.PostAsJsonAsync("/api/notes", new CreateNoteRequest("alice's secret")))
            .Content.ReadFromJsonAsync<NoteDto>();

        Assert.Empty((await bob.GetFromJsonAsync<List<NoteDto>>("/api/notes"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync($"/api/notes/{note!.Id}")).StatusCode);
        Assert.Single((await alice.GetFromJsonAsync<List<NoteDto>>("/api/notes"))!);
    }

    [Fact]
    public async Task Empty_notes_are_refused()
    {
        var client = await app.SignedInClientAsync();
        var response = await client.PostAsJsonAsync("/api/notes", new CreateNoteRequest("   "));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Notes_need_a_sign_in()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.CreateClient().GetAsync("/api/notes")).StatusCode);
    }
}
