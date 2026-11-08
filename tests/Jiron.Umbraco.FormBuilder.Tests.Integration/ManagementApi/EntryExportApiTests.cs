using System.Net;
using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Exports;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Queries;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Exports;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Entries;
using Microsoft.Extensions.Hosting;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// A form's exports through the management API (ROADMAP 11.4, ADR-0210): the dialog's view with the user's last choices,
/// the plan, the streamed file and its headers, a background export that only its owner follows and downloads, and the
/// refusals of a user without <c>export</c> or <c>viewSensitive</c>.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class EntryExportApiTests : ManagementApiTestBase
{
    private const string Admin = "admin";

    private static readonly FormField Salary = new()
    {
        Key = Guid.Parse("c4200000-0000-4000-8000-000000000001"),
        Type = "text",
        Alias = "salary",
        Label = new LocalizedText("Salary"),
        Sensitive = true,
    };

    private IEntryRepository Entries => GetRequiredService<IEntryRepository>();

    [Test]
    public async Task TheView_OffersTheFormats_AndOpensWithTheUsersLastChoices()
    {
        var form = await PublishedAsync();
        await EntryAsync(form, "Ada");

        var first = await CallAsync(Admin, HttpMethod.Get, $"form/{form}/entries/export");
        using var file = await Api.SendAsync(Admin, HttpMethod.Post, $"form/{form}/entries/export/file", new { format = "csv", fields = new[] { NameField.Key }, sensitive = true, options = new { delimiter = "semicolon", headers = "aliases", byteOrderMark = false }, remember = true });
        var again = await CallAsync(Admin, HttpMethod.Get, $"form/{form}/entries/export");
        using var printed = await Api.SendAsync(Admin, HttpMethod.Post, $"form/{form}/entries/export/file", new { format = "print", entries = new[] { await KeyOfAsync(form) } });
        var afterPrint = await CallAsync(Admin, HttpMethod.Get, $"form/{form}/entries/export");
        var handler = await CallAsync("entryHandler", HttpMethod.Get, $"form/{form}/entries/export");

        Assert.Multiple(() =>
        {
            Assert.That(first.Outcome, Is.EqualTo("200"), first.ToString());
            Assert.That(first.Body!["formats"]!.AsArray().Select(format => format!["name"]!.GetValue<string>()), Is.EqualTo(new[] { "csv", "xlsx", "json", "print", "files" }));
            Assert.That(first.Body!["formats"]![0]!["displayName"]!.GetValue<string>(), Is.EqualTo("#jfbExport_formatCsv"));
            Assert.That(first.Body!["formats"]!.AsArray().Select(format => (format!["takesDelimiter"]!.GetValue<bool>(), format["takesHeaders"]!.GetValue<bool>(), format["writesFiles"]!.GetValue<bool>())), Is.EqualTo(new[] { (true, true, false), (false, true, false), (false, false, false), (false, true, false), (false, false, true) }), "csv, xlsx, json, print, files");
            Assert.That(first.Body!["formats"]![1]!["fileName"]!.GetValue<string>(), Does.EndWith("-entries.xlsx"));
            Assert.That(first.Body!["choices"], Is.Null, "nothing exported yet");
            Assert.That(first.Body!["maySeeSensitive"]!.GetValue<bool>(), Is.True);
            Assert.That(file.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(again.Body!["choices"]!.ToJsonString(), Is.EqualTo($$$"""{"format":"csv","fields":["{{{NameField.Key}}}"],"sensitive":true,"options":{"delimiter":"semicolon","headers":"aliases","byteOrderMark":false}}"""));
            Assert.That(printed.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(afterPrint.Body!["choices"]!.ToJsonString(), Is.EqualTo(again.Body!["choices"]!.ToJsonString()), "the entry panel's Print asks for nothing to be remembered");
            Assert.That(handler.Body!["choices"], Is.Null, "each user's own");
            Assert.That(handler.Body!["maySeeSensitive"]!.GetValue<bool>(), Is.False);
        });
    }

    [Test]
    public async Task AFileThatFitsADownload_StreamsAsAnAttachmentNobodyRuns()
    {
        var form = await PublishedAsync();
        await EntryAsync(form, "Ada", salary: "90000");
        await EntryAsync(form, "=HYPERLINK(\"x\")");

        var plan = await CallAsync("entryHandler", HttpMethod.Post, $"form/{form}/entries/export/plan", new { format = "csv" });
        using var answer = await Api.SendAsync("entryHandler", HttpMethod.Post, $"form/{form}/entries/export/file", new { format = "csv", options = new { byteOrderMark = false } });
        var text = await answer.Content.ReadAsStringAsync();
        var rows = ExportFiles.Csv(text);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Body!.ToJsonString(), Is.EqualTo("""{"entries":2,"more":false,"background":false}"""));
            Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.OK), text);
            Assert.That(answer.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/csv"));
            Assert.That(answer.Content.Headers.ContentDisposition?.DispositionType, Is.EqualTo("attachment"));
            Assert.That(answer.Content.Headers.ContentDisposition?.FileName, Does.EndWith("-entries.csv"));
            Assert.That(answer.Headers.GetValues("X-Content-Type-Options"), Is.EqualTo(new[] { "nosniff" }));
            Assert.That(answer.Headers.CacheControl?.NoStore, Is.True, "never kept in a cache");
            Assert.That(rows[0], Does.Not.Contain("Salary"), "no sensitive field unless asked for");
            Assert.That(rows.Skip(1).Select(row => row[4]), Is.EqualTo(new[] { "'=HYPERLINK(\"x\")", "Ada" }), "the formula guarded");
        });
    }

    [Test]
    public async Task SensitiveFields_NeedViewSensitive_AndExportsNeedExport()
    {
        var form = await PublishedAsync();
        await EntryAsync(form, "Ada", salary: "90000");

        var handler = await CallAsync("entryHandler", HttpMethod.Post, $"form/{form}/entries/export/plan", new { format = "csv", sensitive = true });
        var viewer = await CallAsync("viewer", HttpMethod.Post, $"form/{form}/entries/export/plan", new { format = "csv" });
        using var admin = await Api.SendAsync(Admin, HttpMethod.Post, $"form/{form}/entries/export/file", new { format = "csv", sensitive = true, options = new { byteOrderMark = false } });
        var shape = await CallAsync(Admin, HttpMethod.Post, $"form/{form}/entries/export/plan", new { format = "csv", options = new { delimiter = "pipe", headers = "names" } });

        Assert.Multiple(async () =>
        {
            Assert.That((handler.Outcome, handler.Body?["errors"]?[0]?["path"]?.GetValue<string>()), Is.EqualTo(("400 sensitive_not_allowed", "sensitive")), handler.ToString());
            Assert.That(viewer.Outcome, Is.EqualTo("403 forbidden"), viewer.ToString());
            Assert.That(viewer.Body!["permission"]!.GetValue<string>(), Is.EqualTo("export"));
            Assert.That(ExportFiles.Csv(await admin.Content.ReadAsStringAsync())[1], Does.Contain("90000"));
            Assert.That(shape.Body!["errors"]!.AsArray().Select(error => error!["path"]!.GetValue<string>()), Is.EqualTo(new[] { "options.delimiter", "options.headers" }));
        });
    }

    [Test]
    public async Task ABackgroundExport_IsFollowedByItsOwnerOnly_AndDownloadsThroughALinkWithItsTicket()
    {
        var form = await PublishedAsync();
        var another = await PublishedAsync();
        await EntryAsync(form, "Ada");

        var started = await CallAsync("entryHandler", HttpMethod.Post, $"form/{form}/entries/export/jobs", new { format = "json" });
        var key = started.Body!["key"]!.GetValue<Guid>();
        var second = (await CallAsync("entryHandler", HttpMethod.Post, $"form/{form}/entries/export/jobs", new { format = "csv" })).Body!["key"]!.GetValue<Guid>();
        await GetRequiredService<EntryExportJobs>().PassAsync("test", CancellationToken.None);
        var status = await CallAsync("entryHandler", HttpMethod.Get, $"form/{form}/entries/export/jobs/{key}");
        var view = await CallAsync("entryHandler", HttpMethod.Get, $"form/{form}/entries/export");
        var ticket = await CallAsync("entryHandler", HttpMethod.Post, $"form/{form}/entries/export/jobs/{key}/ticket");
        var url = ticket.Body!["url"]!.GetValue<string>();
        var link = url[ManagementApiCaller.BasePath.Length..];
        using var download = await Api.SendAsync(null, link);
        var json = await download.Content.ReadAsStringAsync();
        using var again = await Api.SendAsync(null, link);
        var someoneElse = await CallAsync(Admin, HttpMethod.Get, $"form/{form}/entries/export/jobs/{key}");
        var theirTicket = await CallAsync(Admin, HttpMethod.Post, $"form/{form}/entries/export/jobs/{key}/ticket");
        var withoutTicket = await CallAsync(null, HttpMethod.Get, $"form/{form}/entries/export/jobs/{key}/file");
        var aTokenIsNoTicket = await CallAsync("entryHandler", HttpMethod.Get, $"form/{form}/entries/export/jobs/{key}/file");
        var changed = await CallAsync(null, HttpMethod.Get, link[..^2] + (link.EndsWith("AA", StringComparison.Ordinal) ? "BB" : "AA"));
        var elsewhere = await CallAsync(null, HttpMethod.Get, link.Replace(form.ToString(), another.ToString(), StringComparison.Ordinal));
        var anotherExport = await CallAsync(null, HttpMethod.Get, link.Replace(key.ToString(), second.ToString(), StringComparison.Ordinal));
        var otherForm = await CallAsync("entryHandler", HttpMethod.Get, $"form/{another}/entries/export/jobs/{key}");

        Assert.Multiple(() =>
        {
            Assert.That(started.Outcome, Is.EqualTo("200"), started.ToString());
            Assert.That(started.Body!["state"]!.GetValue<string>(), Is.EqualTo("queued"));
            Assert.That(started.Body!["total"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(status.Body!["state"]!.GetValue<string>(), Is.EqualTo("ready"), status.ToString());
            Assert.That(status.Body!["done"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(status.Body!["expiresUtc"], Is.Not.Null);
            Assert.That(view.Body!["exports"]!.AsArray().Select(export => export!["key"]!.GetValue<Guid>()), Is.EquivalentTo(new[] { key, second }), "the page that comes back finds them");
            Assert.That(ticket.Outcome, Is.EqualTo("200"), ticket.ToString());
            Assert.That(ticket.NoStore, Is.True, "never kept in a cache");
            Assert.That(url, Does.StartWith($"{ManagementApiCaller.BasePath}form/{form}/entries/export/jobs/{key}/file?ticket="));
            Assert.That(ticket.Body!["expiresUtc"]!.GetValue<DateTimeOffset>(), Is.GreaterThan(DateTimeOffset.UtcNow).And.LessThanOrEqualTo(DateTimeOffset.UtcNow + ExportDownloadTickets.Lifetime));
            Assert.That(download.StatusCode, Is.EqualTo(HttpStatusCode.OK), "a link's request, without a token");
            Assert.That(download.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));
            Assert.That(download.Content.Headers.ContentDisposition?.DispositionType, Is.EqualTo("attachment"));
            Assert.That(download.Headers.GetValues("X-Content-Type-Options"), Is.EqualTo(new[] { "nosniff" }));
            Assert.That(download.Headers.CacheControl?.NoStore, Is.True);
            Assert.That(json, Does.Contain("\"name\": \"Ada\""));
            Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.OK), "until the ticket expires");
            Assert.That(someoneElse.Outcome, Is.EqualTo("404 export_not_found"), "an administrator does not see another user's export");
            Assert.That(theirTicket.Outcome, Is.EqualTo("404 export_not_found"), "nor gets its ticket");
            Assert.That(withoutTicket.Outcome, Is.EqualTo("404 export_not_found"));
            Assert.That(aTokenIsNoTicket.Outcome, Is.EqualTo("404 export_not_found"), "only a ticket downloads it");
            Assert.That(changed.Outcome, Is.EqualTo("404 export_not_found"), "a changed ticket");
            Assert.That(elsewhere.Outcome, Is.EqualTo("404 export_not_found"), "the ticket names its form");
            Assert.That(anotherExport.Outcome, Is.EqualTo("404 export_not_found"), "and its export, not another of the same user's");
            Assert.That(otherForm.Outcome, Is.EqualTo("404 export_not_found"));
        });
    }

    [Test]
    public async Task ABackgroundExportWithSensitiveFields_GetsNoTicket_OnceItsOwnerMayNoLongerSeeThem()
    {
        var form = await PublishedAsync();
        await EntryAsync(form, "Ada", salary: "90000");
        var access = GetRequiredService<IFormBuilderAccessService>();
        var group = People.GroupKeys["entryHandler"];
        // viewSensitive goes with viewEntries in a grant.
        await GrantAsync(group, PermissionTarget.Form, form, FormBuilderPermissions.ViewEntries | FormBuilderPermissions.ViewSensitive);

        var started = await CallAsync("entryHandler", HttpMethod.Post, $"form/{form}/entries/export/jobs", new { format = "csv", sensitive = true });
        var key = started.Body!["key"]!.GetValue<Guid>();
        await GetRequiredService<EntryExportJobs>().PassAsync("test", CancellationToken.None);
        var allowed = await CallAsync("entryHandler", HttpMethod.Post, $"form/{form}/entries/export/jobs/{key}/ticket");
        await access.ChangeGrantAsync(new AccessGrantChange(group, PermissionTarget.Form, form, FormBuilderPermissions.ViewEntries | FormBuilderPermissions.ViewSensitive, FormBuilderPermissions.ViewEntries, null));
        var refused = await CallAsync("entryHandler", HttpMethod.Post, $"form/{form}/entries/export/jobs/{key}/ticket");

        Assert.Multiple(() =>
        {
            Assert.That(started.Outcome, Is.EqualTo("200"), started.ToString());
            Assert.That(allowed.Outcome, Is.EqualTo("200"), allowed.ToString());
            Assert.That((refused.Outcome, refused.Body?["permission"]?.GetValue<string>()), Is.EqualTo(("403 forbidden", "viewSensitive")), refused.ToString());
        });
    }

    [Test]
    public async Task AZipOfFiles_AlwaysGoesToTheBackground()
    {
        var form = await PublishedAsync();
        await EntryAsync(form, "Ada");

        var plan = await CallAsync("entryHandler", HttpMethod.Post, $"form/{form}/entries/export/plan", new { format = "files" });
        var ticked = await CallAsync("entryHandler", HttpMethod.Post, $"form/{form}/entries/export/plan", new { format = "files", entries = new[] { await KeyOfAsync(form) } });
        var file = await CallAsync("entryHandler", HttpMethod.Post, $"form/{form}/entries/export/file", new { format = "files" });

        Assert.Multiple(() =>
        {
            Assert.That(plan.Body!.ToJsonString(), Is.EqualTo("""{"entries":1,"more":false,"background":true}"""), "a file of files has no bound");
            Assert.That(ticked.Body!.ToJsonString(), Is.EqualTo("""{"entries":1,"more":false,"background":true}"""));
            Assert.That(file.Outcome, Is.EqualTo("409 export_in_background"), file.ToString());
        });
    }

    [Test]
    public async Task AFileTooLargeForADownload_IsRefused_AndGoesToTheBackgroundInstead()
    {
        var form = await PublishedAsync();
        var search = new { format = "csv", filter = new { search = "person" } };
        await ManyAsync(form, 1_001);

        var plan = await CallAsync("entryHandler", HttpMethod.Post, $"form/{form}/entries/export/plan", search);
        var file = await CallAsync("entryHandler", HttpMethod.Post, $"form/{form}/entries/export/file", search);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Body!.ToJsonString(), Is.EqualTo("""{"entries":1000,"more":true,"background":true}"""), "a search counts up to 1,000");
            Assert.That(file.Outcome, Is.EqualTo("409 export_in_background"), file.ToString());
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.Services.RemoveAll(descriptor => descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType == typeof(EntryExportWorker));
    }

    private async Task<Guid> PublishedAsync()
    {
        var forms = GetRequiredService<IFormBuilderFormService>();
        var created = await forms.CreateAsync(new NewFormRequest($"Form {Guid.NewGuid():N}", UserKey: null));
        var draft = (await forms.GetDraftAsync(created.Key))!;
        var definition = Fillable(draft.Definition!);
        definition = definition with { Pages = [definition.Pages[0] with { Groups = [definition.Pages[0].Groups[0] with { Fields = [.. definition.Pages[0].Groups[0].Fields, Salary] }] }] };
        var saved = (await forms.SaveDraftAsync(created.Key, draft.Form.ConcurrencyVersion, definition, null, null))!;
        await forms.PublishAsync(created.Key, saved.Form.ConcurrencyVersion, null, null);
        return created.Key;
    }

    // An entry with its name and, when given, its salary.
    private async Task EntryAsync(Guid form, string name, string? salary = null)
    {
        var row = (await GetRequiredService<IFormRepository>().GetAsync(form))!;
        var catalogue = await GetRequiredService<IFormRepository>().ListFieldsAsync(row.Id);
        var values = new List<NewEntryValue> { new(catalogue.Single(field => field.Values.FieldKey == NameField.Key).Id, name) };
        if (salary is not null)
        {
            values.Add(new NewEntryValue(catalogue.Single(field => field.Values.FieldKey == Salary.Key).Id, salary));
        }

        _ = await Entries.CreateAsync(new NewEntry
        {
            Key = Guid.NewGuid(),
            FormId = row.Id,
            FormVersionId = row.PublishedVersionId!.Value,
            State = EntryState.Submitted,
            SubmissionId = Guid.NewGuid(),
            SubmissionHash = new string('a', 64),
            Culture = "en-US",
            Values = values,
            SearchText = EntrySearchText.For([name]),
        });
    }

    // The key of the form's newest entry.
    private async Task<Guid> KeyOfAsync(Guid form)
    {
        var row = (await GetRequiredService<IFormRepository>().GetAsync(form))!;
        var ids = await Entries.ListIdsAsync(row.Id, 1000);
        return (await Entries.GetByIdAsync(ids[^1]))!.Key;
    }

    private async Task ManyAsync(Guid form, int count)
    {
        for (var index = 1; index <= count; index++)
        {
            await EntryAsync(form, new StringBuilder("Person ").Append(index).ToString());
        }
    }
}
