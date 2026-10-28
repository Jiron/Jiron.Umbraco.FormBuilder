using System.Net;
using System.Net.Http.Headers;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Projection;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Web.PublicApi;
using Umbraco.Cms.Tests.Common.Testing;
using static Jiron.Umbraco.FormBuilder.Tests.Integration.Forms.FormServiceTesting;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.PublicApi;

/// <summary>
/// A host's designer lock reaches visitors (ADR-0060, ADR-0084): a form published before the lock came is served with the
/// lock's value, under another <c>ETag</c> than its answer before the lock, so a client that holds that one gets the new
/// answer.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class PublicFormLockTests : PublicApiTestBase
{
    private const string Locks = FormBuilderOptions.SectionName + ":Designer:SettingLocks:";

    [Test]
    public async Task AFormPublishedBeforeALock_IsServedWithTheLocksValue_UnderANewETag()
    {
        var key = await BeforeTheLockAsync();
        var draft = (await Forms.GetDraftAsync(key))!;
        _ = await Forms.PublishAsync(key, draft.Form.ConcurrencyVersion, null, Editor);
        var stored = await GetRequiredService<IFormRepository>().GetVersionAsync((await GetRequiredService<IFormRepository>().GetAsync(key))!.Id, 2);

        // The answer the form had before the lock: its own value, as stored.
        var running = (await Forms.GetPublishedAsync(key))!;
        var unlocked = running with { Definition = WithFields(running.Definition, NameField with { Settings = SettingsObject.Parse("""{"maxLength":500}""") }) };
        var before = await GetRequiredService<FormRequests>().DefinitionAsync(
            new CachedForm(unlocked, PublicProjection.Create(unlocked.Definition, GetRequiredService<FieldTypeRegistry>())), "en-US", new RequestPage(null, null), CancellationToken.None);

        using var response = await GetAsync($"{Root}/forms/{key}", request => request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(before.ETag)));
        var field = (await JsonAsync(response))["form"]!["pages"]![0]!["groups"]![0]!["fields"]![0]!;

        Assert.Multiple(() =>
        {
            Assert.That(stored?.Definition, Does.Contain("\"maxLength\":500"), "nothing was rewritten");
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), "the client's answer from before the lock is not the current one");
            Assert.That(field["settings"]!["maxLength"]!.GetValue<int>(), Is.EqualTo(100));
            Assert.That(response.Headers.ETag?.Tag, Is.Not.EqualTo(before.ETag));
        });
    }

    /// <inheritdoc />
    protected override void ConfigureHost(IDictionary<string, string> configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration[Locks + "0:Kind"] = "field";
        configuration[Locks + "0:Type"] = "text";
        configuration[Locks + "0:Setting"] = "maxLength";
        configuration[Locks + "0:Value"] = "100";
    }

    // A headless form whose draft a server without the lock stored, with its own maximum length.
    private async Task<Guid> BeforeTheLockAsync()
    {
        var created = await Forms.CreateAsync(new NewFormRequest("Contact us", UserKey: Editor));
        var blank = (await Forms.GetDraftAsync(created.Key))!.Definition!;
        var repository = GetRequiredService<IFormRepository>();
        var row = (await repository.GetAsync(created.Key))!;
        _ = await repository.SaveDraftAsync(row.Id, row.ConcurrencyVersion, Stored(Headless(WithFields(blank, NameField with { Settings = SettingsObject.Parse("""{"maxLength":500}""") }))), null, Editor);
        return created.Key;
    }
}
