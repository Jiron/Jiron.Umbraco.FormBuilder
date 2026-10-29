using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Rendering;

/// <summary>
/// A page with a form while the migrations are pending, behind Umbraco's website output cache (ROADMAP 6.2l, ADR-0095,
/// ADR-0104): a visitor sees nothing of the form and a signed-in backoffice user the note, so the page is never stored
/// for either, and neither page answers for the other. The pages are asked for over https, where the backoffice's cookie
/// travels.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class OutputCachePendingTests : OutputCacheTestBase
{
    protected override bool RunsTheMigrations => false;

    [Test]
    public async Task WhileTheMigrationsArePending_APageWithAForm_IsNeverStored_ForAVisitor_OrForABackofficeUser()
    {
        _ = await PagesAsync(CachePage);
        const string Address = "https://localhost/cache/";

        var visitor = await ServedAsync(Visitor(secure: true), Address);
        var again = await ServedAsync(Visitor(secure: true), Address);
        await SignInBackOfficeAsync();
        var editor = await ServedAsync(Client, Address);
        var editorAgain = await ServedAsync(Client, Address);
        var after = await ServedAsync(Visitor(secure: true), Address);

        Assert.Multiple(() =>
        {
            Served[] all = [visitor, again, editor, editorAgain, after];
            Assert.That(all.Select(served => served.CacheControl), Is.All.EqualTo("no-store"));
            Assert.That(all.Select(served => served.FromCache), Is.All.False);
            Assert.That(all.Select(served => served.Stamp).Distinct().Count(), Is.EqualTo(all.Length), "each one rendered");
            Assert.That(visitor.Document.QuerySelectorAll(".jfb-notice, form"), Is.Empty, "nothing of the form for a visitor");
            Assert.That(editor.Document.QuerySelectorAll(".jfb-notice"), Has.Length.EqualTo(1), "the note for a backoffice user");
            Assert.That(after.Document.QuerySelectorAll(".jfb-notice"), Is.Empty, "the note never answers a visitor");
        });
    }
}
