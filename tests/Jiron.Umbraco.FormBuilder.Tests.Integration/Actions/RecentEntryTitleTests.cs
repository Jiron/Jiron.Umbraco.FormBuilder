using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Actions;

/// <summary>How the Overview names an entry (ROADMAP 9.1, ADR-0165): by the form's entry title, with sensitive answers left out.</summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class RecentEntryTitleTests : ActionTestBase
{
    private static readonly AccessUser Administrator = new(Guid.NewGuid(), new HashSet<Guid>(), true, true);

    private static readonly FormField Salary = new()
    {
        Key = Guid.Parse("c4000000-0000-4000-8000-000000000090"),
        Type = "text",
        Alias = "salary",
        Label = new LocalizedText("Salary"),
        Sensitive = true,
    };

    private Task<Guid> TitledAsync(string? title) => PublishAsync(
        name: "Titled",
        change: definition => definition with { Settings = definition.Settings with { Submission = definition.Settings.Submission with { EntryTitle = title } } },
        fields: [NameField, Salary]);

    private async Task<RecentEntry> OnlyAsync() => (await GetRequiredService<RecentEntries>().ListAsync(Administrator)).Single();

    [Test]
    public async Task AnEntry_IsNamedByTheFormsTitle()
    {
        await SubmitAsync(await TitledAsync("{{ field.name }}"), """{"name":"Anna Svensson"}""");

        Assert.That((await OnlyAsync()).Title, Is.EqualTo("Anna Svensson"));
    }

    [Test]
    public async Task ASensitiveAnswer_StaysOutOfTheTitle()
    {
        await SubmitAsync(await TitledAsync("{{ field.name }} {{ field.salary }}"), """{"name":"Anna","salary":"50000"}""");

        Assert.That((await OnlyAsync()).Title, Is.EqualTo("Anna"));
    }

    [Test]
    public async Task ATitleThatComesOutEmpty_LeavesTheNumberToNameIt()
    {
        await SubmitAsync(await TitledAsync("{{ field.salary }}"), """{"name":"Anna","salary":"50000"}""");

        Assert.That((await OnlyAsync()).Title, Is.Null);
    }

    [Test]
    public async Task AFormWithoutATitle_LeavesTheNumberToNameIt()
    {
        var entry = await SubmitAsync(await TitledAsync(null), """{"name":"Anna"}""");
        var recent = await OnlyAsync();

        Assert.Multiple(() =>
        {
            Assert.That(recent.Title, Is.Null);
            Assert.That(recent.Number, Is.EqualTo(entry.Number));
        });
    }
}
