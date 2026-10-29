using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Umbraco.Cms.Core;

namespace Jiron.Umbraco.FormBuilder.Demo.Seeding;

/// <summary>
/// Creates the demo's forms when they are missing (ADR-0094): each from its definition in <c>Seeding/Forms/</c>, in the
/// package's definition format, found again by its alias, then created, saved and published through the form service
/// as an editor's would be. A form that exists is never changed, so what an editor does on the demo stays until a
/// reset. The pages name their forms in <c>DemoContent.json</c>. Umbraco runs the package's migrations in the
/// background after an install or an upgrade, so the forms wait for them.
/// </summary>
internal sealed class DemoFormSeeder(
    IFormBuilderFormService forms,
    FormDefinitionSerializer serializer,
    IFormBuilderMigrationState migrations,
    TimeProvider time)
{
    private const string ResourcePrefix = "Jiron.Umbraco.FormBuilder.Demo.Forms.";
    private const string Comment = "Created by the demo's seeder";
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
    ];

    /// <summary>Creates and publishes each missing form; true when one was created.</summary>
    /// <param name="cancellationToken">Cancels the seeding.</param>
    public async Task<bool> SeedAsync(CancellationToken cancellationToken)
    {
        await MigratedAsync(cancellationToken);
        var created = false;
        foreach (var (alias, name) in Forms)
        {
            if (await forms.GetByAliasAsync(alias, cancellationToken) is not null)
            {
                continue;
            }

            var definition = await ReadAsync(alias, cancellationToken);
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
