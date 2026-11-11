using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Testing.AI.FakeAI;
using Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

namespace Jiron.Umbraco.FormBuilder.Tests.AI;

/// <summary>
/// Umbraco's global set-up for these integration tests (the test database pool, the working directory). NUnit runs
/// set-up fixtures only from the test assembly, so this wrapper calls Umbraco's.
/// </summary>
[SetUpFixture]
public sealed class AITestSetup
{
    private readonly GlobalSetupTeardown _umbraco = new();

    [OneTimeSetUp]
    public void SetUp() => _umbraco.SetUp();

    [OneTimeTearDown]
    public void TearDown() => _umbraco.TearDown();
}

/// <summary>
/// The management API on a real site with Jiron Form Builder, the AI add-on and Umbraco.AI on the fake provider (D22,
/// ADR-0018). Umbraco.AI keeps its data with EF Core apart from Umbraco's database, so each test gets an Umbraco.AI
/// SQLite file of its own; Umbraco.AI creates its tables as the site starts. Each test starts with the fake chat profile
/// chosen in Settings > AI and an empty journal.
/// </summary>
public abstract class AITestBase : ManagementApiTestBase
{
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "jfb-ai-tests");

    /// <summary>What the fake model received in this test.</summary>
    protected FakeChatJournal Journal => GetRequiredService<FakeChatJournal>();

    /// <summary>Gives the host its own Umbraco.AI database.</summary>
    protected override void ConfigureHost(IDictionary<string, string> configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        base.ConfigureHost(configuration);
        Directory.CreateDirectory(Folder);
        foreach (var stale in Directory.EnumerateFiles(Folder).Where(file => File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddHours(-1)))
        {
            try
            {
                File.Delete(stale);
            }
            catch (IOException)
            {
                // Still open; a later run removes it.
            }
        }

        configuration["ConnectionStrings:umbracoAIDbDSN"] = $"Data Source={Path.Combine(Folder, $"{Guid.NewGuid():N}.sqlite.db")};Cache=Shared;Foreign Keys=True;Pooling=True";
        configuration["ConnectionStrings:umbracoAIDbDSN_ProviderName"] = "Microsoft.Data.Sqlite";
        // Offered, not chosen: each test chooses the fake profile itself, or not (ChoosesTheFakeProfile).
        configuration["JironFormBuilderTesting:FakeAI"] = "Offered";
    }

    /// <summary>Whether a test starts with the fake profile chosen; a test of a site without one says no.</summary>
    protected virtual bool ChoosesTheFakeProfile => true;

    /// <summary>The fake chat profile, chosen in Settings > AI, and an empty journal.</summary>
    [SetUp]
    public async Task UseTheFakeProviderAsync()
    {
        await GetRequiredService<FakeAISetup>().EnsureAsync(ChoosesTheFakeProfile);
        Journal.Clear();
    }

    /// <summary>A form whose saved draft holds these fields in its first group.</summary>
    protected async Task<Guid> FormWithAsync(params FormField[] fields)
    {
        var forms = GetRequiredService<IFormBuilderFormService>();
        var key = (await forms.CreateAsync(new NewFormRequest(Guid.NewGuid().ToString("N")))).Key;
        var draft = (await forms.GetDraftAsync(key))!;
        var definition = draft.Definition!;
        definition = definition with { Pages = [definition.Pages[0] with { Groups = [definition.Pages[0].Groups[0] with { Fields = fields }] }] };
        _ = await forms.SaveDraftAsync(key, draft.Form.ConcurrencyVersion, definition, null, null);
        return key;
    }

    /// <summary>A field of a type with a label, and a help text when one is given.</summary>
    protected static FormField FieldOf(string type, string alias, LocalizedText label, string? help = null) => new()
    {
        Key = Guid.NewGuid(),
        Type = type,
        Alias = alias,
        Label = label,
        Help = help is null ? null : new LocalizedText(help),
    };

    /// <summary>How many audit entries of an action there are.</summary>
    internal async Task<long> AuditCountAsync(string action) =>
        (await GetRequiredService<IAuditRepository>().QueryAsync(new AuditQuery { Action = action }, new PageRequest(0, 100))).Total;
}
