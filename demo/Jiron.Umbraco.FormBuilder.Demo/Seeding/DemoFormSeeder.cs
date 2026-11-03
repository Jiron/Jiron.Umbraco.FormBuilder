using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;

namespace Jiron.Umbraco.FormBuilder.Demo.Seeding;

/// <summary>
/// Creates the demo's forms when they are missing (ADR-0094): each from its definition in <c>Seeding/Forms/</c>, in the
/// package's definition format, found again by its alias, then created, saved and published through the form service
/// as an editor's would be. A form that exists is never changed, so what an editor does on the demo stays until a
/// reset. The pages name their forms in <c>DemoContent.json</c>, by alias, in their text property or a form block.
/// Umbraco runs the package's migrations in the background after an install or an upgrade, so the forms wait for them.
/// A host whose designer locks fix or hide settings (ADR-0060) gets the forms without those settings: the site uses the
/// locks' values, and a save that leaves a locked setting out is never refused. The library gets one manual option list,
/// so the designer's source picker has one to offer (ADR-0172); like a form, it is created only when its alias is free.
/// </summary>
internal sealed class DemoFormSeeder(
    IFormBuilderFormService forms,
    OptionListService lists,
    FormDefinitionSerializer serializer,
    IFormBuilderMigrationState migrations,
    IOptions<FormBuilderOptions> options,
    TimeProvider time)
{
    private const string ResourcePrefix = "Jiron.Umbraco.FormBuilder.Demo.Forms.";
    private const string Comment = "Created by the demo's seeder";

    /// <summary>The library's option list the designer's source picker offers (ADR-0172), in English and German.</summary>
    private const string Branches = """
        [
          {"value":"central","label":{"":"Central library","de-DE":"Zentralbibliothek"}},
          {"value":"harbour","label":{"":"Harbour branch","de-DE":"Zweigstelle Hafen"}},
          {"value":"hillside","label":{"":"Hillside branch","de-DE":"Zweigstelle am Berg"}}
        ]
        """;

    private static readonly Guid User = Constants.Security.SuperUserKey;
    private static readonly TimeSpan MigrationsDeadline = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MigrationsPoll = TimeSpan.FromSeconds(1);

    /// <summary>The demo's forms: the alias, which names the definition's file too, and the name.</summary>
    private static readonly (string Alias, string Name)[] Forms =
    [
        ("contact", "Contact"),
        ("workshop", "Workshop booking"),
        ("feedback", "Feedback"),
        ("roomHire", "Room hire"),
        ("newsletter", "Newsletter"),
        ("application", "Job application"),
        ("specimen", "Specimen"),
        ("volunteer", "Volunteer sign-up"),
    ];

    /// <summary>Creates the option list and creates and publishes each missing form; true when something was created.</summary>
    /// <param name="cancellationToken">Cancels the seeding.</param>
    public async Task<bool> SeedAsync(CancellationToken cancellationToken)
    {
        await MigratedAsync(cancellationToken);
        var created = await SeedListAsync(cancellationToken);
        foreach (var (alias, name) in Forms)
        {
            if (await forms.GetByAliasAsync(alias, cancellationToken) is not null)
            {
                continue;
            }

            var definition = WithoutLockedSettings(await ReadAsync(alias, cancellationToken), options.Value.Designer.SettingLocks);
            var form = await forms.CreateAsync(new NewFormRequest(name, alias, UserKey: User), cancellationToken);
            var draft = await forms.GetDraftAsync(form.Key, cancellationToken)
                ?? throw new InvalidOperationException($"Demo seeding: the form {alias} has no draft.");
            var saved = await forms.SaveDraftAsync(form.Key, draft.Form.ConcurrencyVersion, definition with { Key = form.Key }, Comment, User, cancellationToken)
                ?? throw new InvalidOperationException($"Demo seeding: the form {alias} was not saved.");
            _ = await forms.PublishAsync(form.Key, saved.Form.ConcurrencyVersion, Comment, User, cancellationToken)
                ?? throw new InvalidOperationException($"Demo seeding: the form {alias} was not published.");
            created = true;
        }

        return created;
    }

    // The library's branches, once: a list the alias names already is the demo's, as an editor may have changed it.
    private async Task<bool> SeedListAsync(CancellationToken cancellationToken)
    {
        var result = await lists.CreateAsync(Guid.NewGuid(), new OptionListInput("branches", "Library branches", OptionListKind.Manual, Items: Branches), cancellationToken);
        if (!result.Succeeded && result.Problems.Any(problem => problem.Code != DefinitionErrors.AliasDuplicate))
        {
            throw new InvalidOperationException($"Demo seeding: the option list was refused: {result.Problems[0].Message}");
        }

        return result.Succeeded;
    }

    /// <summary>The key of a demo form, which the pages' form blocks pick (ADR-0136).</summary>
    /// <param name="alias">The form's alias.</param>
    /// <param name="cancellationToken">Cancels the seeding.</param>
    public async Task<Guid> KeyOfAsync(string alias, CancellationToken cancellationToken) =>
        (await forms.GetByAliasAsync(alias, cancellationToken))?.Key
        ?? throw new InvalidOperationException($"Demo seeding: a page's form block picks the form {alias}, which is missing.");

    /// <summary>
    /// A definition without the settings that the host's locks of kind <c>field</c> fix or hide, child fields of a
    /// repeatable group included; the same instance when the host has no such lock.
    /// </summary>
    /// <param name="definition">The demo form's definition.</param>
    /// <param name="locks">The host's designer locks.</param>
    internal static FormDefinition WithoutLockedSettings(FormDefinition definition, IEnumerable<DesignerSettingLock> locks)
    {
        var locked = locks
            .Where(each => string.Equals(each.Kind, DesignerSettingLock.FieldKind, StringComparison.OrdinalIgnoreCase) && each.Type is not null && each.Setting is not null)
            .ToLookup(each => each.Type!, each => each.Setting!, StringComparer.Ordinal);
        if (locked.Count == 0)
        {
            return definition;
        }

        FormField Strip(FormField field)
        {
            var settings = field.Settings;
            if (settings is not null && locked.Contains(field.Type))
            {
                var own = JsonNode.Parse(settings.Value.GetRawText())!.AsObject();
                foreach (var name in locked[field.Type])
                {
                    own.Remove(name);
                }

                settings = own.Count == 0 ? null : SettingsObject.Parse(own.ToJsonString());
            }

            return field with { Settings = settings, Fields = [.. field.Fields.Select(Strip)] };
        }

        return definition with
        {
            Pages = [.. definition.Pages.Select(page => page with { Groups = [.. page.Groups.Select(group => group with { Fields = [.. group.Fields.Select(Strip)] })] })],
        };
    }

    private async Task MigratedAsync(CancellationToken cancellationToken)
    {
        var deadline = time.GetUtcNow() + MigrationsDeadline;
        while (await migrations.IsPendingAsync(cancellationToken))
        {
            if (time.GetUtcNow() > deadline)
            {
                throw new InvalidOperationException("Demo seeding: Jiron Form Builder's migrations did not finish within two minutes, so the demo forms were not created; restart the demo.");
            }

            await Task.Delay(MigrationsPoll, time, cancellationToken);
        }
    }

    private async Task<FormDefinition> ReadAsync(string alias, CancellationToken cancellationToken)
    {
        await using var stream = typeof(DemoFormSeeder).Assembly.GetManifestResourceStream(ResourcePrefix + alias + ".json")
            ?? throw new InvalidOperationException($"Demo seeding: the definition of the form {alias} is not embedded.");
        using var reader = new StreamReader(stream);
        var read = serializer.Read(await reader.ReadToEndAsync(cancellationToken));
        return read.Definition
            ?? throw new InvalidOperationException($"Demo seeding: the definition of the form {alias} does not read: {string.Join("; ", read.Problems.Select(problem => $"{problem.Path}: {problem.Message}"))}");
    }
}
