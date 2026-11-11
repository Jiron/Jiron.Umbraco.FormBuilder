using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Rendering;

/// <summary>
/// A page whose form cannot render while the migrations are pending (ROADMAP 6.2c, ADR-0033): it answers as usual
/// without the form for a visitor, and with a short note in the form's place for a signed-in backoffice user. The plan
/// stays behind for the whole test, and the note is in the page's culture.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class FormRenderingPendingTests : RenderingTestBase
{
    protected override bool RunsTheMigrations => false;

    [Test]
    public async Task WhileTheMigrationsArePending_APageRendersWithoutItsForm_ForAVisitor_AndWithANoteForABackofficeUser()
    {
        _ = await PagesAsync("pending");
        Assume.That(await GetRequiredService<IFormBuilderMigrationState>().IsPendingAsync(), Is.True);

        var (_, visitor) = await PageAtAsync("localhost", "pending", secure: true);
        await SignInBackOfficeAsync();
        var (_, editor) = await PageAtAsync("localhost", "pending", secure: true);

        Assert.Multiple(() =>
        {
            Assert.That(visitor.QuerySelector("main p")!.TextContent, Is.EqualTo("The page's own text."), "the rest of the page renders as usual");
            Assert.That(visitor.QuerySelectorAll("[class^=jfb], [class*=' jfb'], script[src*='/jfb/']"), Is.Empty, "nothing of the form for a visitor");
            Assert.That(editor.QuerySelector("main p")!.TextContent, Is.EqualTo("The page's own text."));
            Assert.That(editor.QuerySelectorAll(".jfb-notice").Select(note => (note.QuerySelector("h2")!.TextContent, note.QuerySelector("p")!.TextContent)).ToArray(), Is.EqualTo(new[]
            {
                ("This form waits for an update", "Visitors see nothing here until Jiron Form Builder's database update has run. Only signed-in backoffice users see this note."),
            }), "one note in the form's place for a backoffice user");
            Assert.That(editor.QuerySelectorAll("form, script[src*='/jfb/']"), Is.Empty, "the note brings no form and no assets");
        });
    }

    [Test]
    public async Task WhileTheMigrationsArePending_TheNoteIsInThePagesCulture()
    {
        _ = await CulturePagesAsync("de-DE", "German", "pending");
        Assume.That(await GetRequiredService<IFormBuilderMigrationState>().IsPendingAsync(), Is.True);
        await SignInBackOfficeAsync();
        var (_, editor) = await PageAtAsync("localhost", "pending", secure: true);

        Assert.That(editor.QuerySelectorAll(".jfb-notice").Select(note => (note.QuerySelector("h2")!.TextContent, note.QuerySelector("p")!.TextContent)).ToArray(), Is.EqualTo(new[]
        {
            ("Dieses Formular wartet auf ein Update", "Auf der Website ist hier nichts zu sehen, bis das Datenbank-Update von Jiron Form Builder ausgef" + (char)0xFC + "hrt wurde. Diesen Hinweis sieht nur, wer im Backoffice angemeldet ist."),
        }));
    }
}
