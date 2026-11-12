using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Email;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Jiron.Umbraco.FormBuilder.Testing.Endpoints;

/// <summary>
/// The load host's seeding (ROADMAP 16.2, ADR-0238), behind the same gate as every test endpoint: the forms the load script
/// measures, a form's entries sent through the submission pipeline as a server key sends them, and a folder of many forms
/// for the tree.
/// </summary>
internal static class LoadEndpoints
{
    /// <summary>The address the load forms' mail goes to.</summary>
    public const string Team = "team@load.jfb.test";

    // How many entries a load request sends at a time: SQLite has one writer, so more only wait (Tandem ADR-0063).
    private const int AtATime = 4;

    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;

    private static readonly string[] Countries = ["CH", "DE", "AT", "FR", "IT", "NL", "BE", "DK", "ES", "PT", "SE", "NO", "FI", "PL", "CZ", "GB", "IE", "US", "CA", "AU"];

    private static readonly string[] Topics = ["Bookings", "Membership", "Events", "Press", "Other"];

    /// <summary>
    /// <c>POST /jfb-testing/load-forms</c>: publishes the load script's three headless forms and answers their keys: the
    /// entries form (twelve fields of the common types, as the entry spike measured, ADR-0003), the wide form (fifty
    /// fields, for rendering), and the upload form (a name, an email address, a reference and a file, with a mail to the
    /// team that names the reference).
    /// </summary>
    /// <param name="context">The request.</param>
    public static async Task FormsAsync(HttpContext context)
    {
        var entries = await PublishAsync(context, "Load entries", EntryFields(), actions: []);
        var wide = await PublishAsync(context, "Load wide", WideFields(), actions: []);
        var mail = new FormAction
        {
            Key = Guid.NewGuid(),
            Type = EmailActionType.Alias,
            Name = "Tell the team",
            Settings = SettingsObject.Parse(JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["to"] = new[] { Team },
                ["subject"] = new Dictionary<string, string> { [""] = "Load {{ field.reference }}" },
                ["body"] = new Dictionary<string, string> { [""] = "<p>{{ field.name }} sent the form.</p>" },
            })),
        };
        FormField[] uploadFields =
        [
            new() { Key = Guid.NewGuid(), Type = "text", Alias = "name", Label = new LocalizedText("Name"), Required = true },
            new() { Key = Guid.NewGuid(), Type = "email", Alias = "email", Label = new LocalizedText("Email address"), Required = true },
            new() { Key = Guid.NewGuid(), Type = "text", Alias = "reference", Label = new LocalizedText("Reference") },
            new() { Key = Guid.NewGuid(), Type = "file", Alias = "plan", Label = new LocalizedText("Plan") },
        ];
        var upload = await PublishAsync(context, "Load upload", uploadFields, actions: [mail]);
        await context.Response.WriteAsJsonAsync(
            new
            {
                entries = new { key = entries.Key, revision = entries.Revision, fields = entries.Fields },
                wide = new { key = wide.Key, revision = wide.Revision, fields = wide.Fields },
                upload = new { key = upload.Key, revision = upload.Revision, fields = upload.Fields },
            },
            Json,
            context.RequestAborted);
    }

    /// <summary>
    /// <c>POST /jfb-testing/load-entries {"form": "&lt;key&gt;", "from": 0, "count": 5000}</c>: sends that many entries of the
    /// entries form through the submission pipeline, numbered from <c>from</c>, four at a time, as a server key sends them, and
    /// answers how long it took. At most 20,000 a request.
    /// </summary>
    /// <param name="context">The request.</param>
    public static async Task EntriesAsync(HttpContext context)
    {
        var body = await JsonSerializer.DeserializeAsync<EntriesRequest>(context.Request.Body, Json, context.RequestAborted);
        var count = Math.Clamp(body?.Count ?? 0, 0, 20_000);
        var from = Math.Max(0, body?.From ?? 0);
        var form = body?.Form ?? throw new InvalidOperationException("The request names no form.");
        var published = await context.RequestServices.GetRequiredService<IFormBuilderFormService>().GetAsync(form, context.RequestAborted)
            ?? throw new InvalidOperationException("The form is not there.");
        var submissions = context.RequestServices.GetRequiredService<FormSubmissions>();
        var watch = Stopwatch.StartNew();
        await Parallel.ForEachAsync(
            Enumerable.Range(from, count),
            new ParallelOptions { MaxDegreeOfParallelism = AtATime, CancellationToken = context.RequestAborted },
            async (number, cancellationToken) =>
            {
                var result = await submissions.SubmitAsync(
                    new SubmissionRequest
                    {
                        Form = form,
                        Revision = published.PublicRevision,
                        SubmissionId = Guid.NewGuid(),
                        Culture = "en-US",
                        ServerKey = "load-seed",
                        Fields = EntryAnswers(number),
                    },
                    cancellationToken);
                if (result.Receipt is null)
                {
                    throw new InvalidOperationException($"The load host could not send entry {number}: {result.Refusal}.");
                }
            });
        await context.Response.WriteAsJsonAsync(new { sent = count, milliseconds = watch.ElapsedMilliseconds }, Json, context.RequestAborted);
    }

    /// <summary>
    /// <c>POST /jfb-testing/load-tree {"count": 1000, "name": "..."}</c>: a folder of that name with that many forms, as the
    /// tree's budget counts them; answers the folder's key and name and how long it took. At most 2,000.
    /// </summary>
    /// <param name="context">The request.</param>
    public static async Task TreeAsync(HttpContext context)
    {
        var body = await JsonSerializer.DeserializeAsync<TreeRequest>(context.Request.Body, Json, context.RequestAborted);
        var count = Math.Clamp(body?.Count ?? 0, 0, 2000);
        var folders = context.RequestServices.GetRequiredService<IFormBuilderFolderService>();
        var forms = context.RequestServices.GetRequiredService<IFormBuilderFormService>();
        var watch = Stopwatch.StartNew();
        var folder = await folders.CreateAsync(new NewFolderRequest(body?.Name ?? $"Load tree {Guid.NewGuid():N}"), context.RequestAborted);
        for (var number = 1; number <= count; number++)
        {
            _ = await forms.CreateAsync(new NewFormRequest(string.Create(CultureInfo.InvariantCulture, $"Load form {number:0000}"), FolderKey: folder.Key), context.RequestAborted);
        }

        await context.Response.WriteAsJsonAsync(new { folder = folder.Key, name = folder.Name, milliseconds = watch.ElapsedMilliseconds }, Json, context.RequestAborted);
    }

    // The entries form's twelve fields.
    private static FormField[] EntryFields() =>
    [
        new() { Key = Guid.NewGuid(), Type = "text", Alias = "name", Label = new LocalizedText("Name"), Required = true },
        new() { Key = Guid.NewGuid(), Type = "email", Alias = "email", Label = new LocalizedText("Email address"), Required = true },
        new() { Key = Guid.NewGuid(), Type = "text", Alias = "city", Label = new LocalizedText("City") },
        Choice("country", "Country", Countries),
        Choice("position", "Position", [.. Enumerable.Range(1, 10).Select(index => string.Create(CultureInfo.InvariantCulture, $"Position {index}"))]),
        new() { Key = Guid.NewGuid(), Type = "number", Alias = "amount", Label = new LocalizedText("Amount") },
        new() { Key = Guid.NewGuid(), Type = "number", Alias = "age", Label = new LocalizedText("Age") },
        new() { Key = Guid.NewGuid(), Type = "date", Alias = "started", Label = new LocalizedText("Started") },
        new() { Key = Guid.NewGuid(), Type = "checkbox", Alias = "newsletter", Label = new LocalizedText("Newsletter") },
        new() { Key = Guid.NewGuid(), Type = "textarea", Alias = "message", Label = new LocalizedText("Message") },
        new() { Key = Guid.NewGuid(), Type = "phone", Alias = "phone", Label = new LocalizedText("Phone") },
        Choice("topic", "Topic", Topics, "radio"),
    ];

    // The answers of entry number n: spread over the choices, a date in the last three years, every fifth without a phone.
    private static Dictionary<string, PostedAnswer> EntryAnswers(int number)
    {
        var day = new DateOnly(2023, 11, 1).AddDays(number % 1095);
        var answers = new Dictionary<string, PostedAnswer>
        {
            ["name"] = new([string.Create(CultureInfo.InvariantCulture, $"Person {number:000000}")]),
            ["email"] = new([string.Create(CultureInfo.InvariantCulture, $"person{number}@load.jfb.test")]),
            ["city"] = new([string.Create(CultureInfo.InvariantCulture, $"City {number % 400}")]),
            ["country"] = new([$"o{(number % Countries.Length) + 1}"]),
            ["position"] = new([$"o{(number % 10) + 1}"]),
            ["amount"] = new([(number % 5000).ToString(CultureInfo.InvariantCulture)]),
            ["age"] = new([(18 + (number % 60)).ToString(CultureInfo.InvariantCulture)]),
            ["started"] = new([day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)]),
            ["message"] = new([string.Create(CultureInfo.InvariantCulture, $"A message of person {number}, about {Topics[number % Topics.Length].ToLowerInvariant()}, with a few words more to search.")]),
            ["topic"] = new([$"o{(number % Topics.Length) + 1}"]),
        };
        if (number % 3 == 0)
        {
            answers["newsletter"] = new(["true"]);
        }

        if (number % 5 != 0)
        {
            answers["phone"] = new([string.Create(CultureInfo.InvariantCulture, $"+41 44 {number % 1000:000} {number % 100:00} {number % 97:00}")]);
        }

        return answers;
    }

    // Fifty fields of the types a long form holds, in five groups.
    private static FormField[] WideFields()
    {
        string[] types = ["text", "email", "textarea", "number", "date", "phone", "url", "checkbox"];
        var fields = new List<FormField>();
        for (var number = 1; number <= 50; number++)
        {
            var label = string.Create(CultureInfo.InvariantCulture, $"Question {number}");
            var alias = string.Create(CultureInfo.InvariantCulture, $"answer{number}");
            fields.Add((number % 10) switch
            {
                9 => Choice(alias, label, Topics),
                0 => Choice(alias, label, Topics, "radio"),
                _ => new FormField { Key = Guid.NewGuid(), Type = types[(number - 1) % types.Length], Alias = alias, Label = new LocalizedText(label) },
            });
        }

        return [.. fields];
    }

    private static FormField Choice(string alias, string label, string[] options, string type = "dropdown") => new()
    {
        Key = Guid.NewGuid(),
        Type = type,
        Alias = alias,
        Label = new LocalizedText(label),
        Options = new FieldOptions { Items = [.. options.Select((option, index) => new FieldOption { Value = $"o{index + 1}", Label = new LocalizedText(option) })] },
    };

    // A headless form with these fields, in groups of ten on one page, published.
    private static async Task<Published> PublishAsync(HttpContext context, string name, FormField[] fields, List<FormAction> actions)
    {
        var forms = context.RequestServices.GetRequiredService<IFormBuilderFormService>();
        var created = await forms.CreateAsync(new NewFormRequest($"{name} {Guid.NewGuid():N}"), context.RequestAborted);
        var draft = (await forms.GetDraftAsync(created.Key, cancellationToken: context.RequestAborted))!;
        var definition = draft.Definition!;
        var groups = fields.Chunk(10).Select(chunk => new FormGroup { Key = Guid.NewGuid(), Fields = [.. chunk] }).ToList();
        var saved = (await forms.SaveDraftAsync(
            created.Key,
            draft.Form.ConcurrencyVersion,
            definition with
            {
                Pages = [definition.Pages[0] with { Groups = groups }],
                Settings = definition.Settings with { Developer = definition.Settings.Developer with { Headless = true } },
                Actions = actions,
            },
            null,
            null,
            context.RequestAborted))!;
        _ = await forms.PublishAsync(created.Key, saved.Form.ConcurrencyVersion, null, null, context.RequestAborted);
        var published = (await forms.GetAsync(created.Key, context.RequestAborted))!;
        return new Published(created.Key, published.PublicRevision, fields.ToDictionary(field => field.Alias ?? field.Key.ToString("N"), field => field.Key));
    }

    private sealed record Published(Guid Key, int Revision, Dictionary<string, Guid> Fields);

    private sealed record EntriesRequest(Guid? Form, int? From, int? Count);

    private sealed record TreeRequest(int? Count, string? Name);
}
