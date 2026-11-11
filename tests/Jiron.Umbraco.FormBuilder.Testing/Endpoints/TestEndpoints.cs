using System.Net;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Email;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Core.Signatures;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Testing.Stub;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Services;

namespace Jiron.Umbraco.FormBuilder.Testing.Endpoints;

/// <summary>
/// When the test-only endpoints answer (ADR-0011, ADR-0029): all three must hold, so a switch left on by mistake opens
/// nothing to the network.
/// <list type="number">
/// <item><description>The host turned them on (<c>JironFormBuilderTesting:Endpoints</c>).</description></item>
/// <item><description>The host runs in the Development environment; checked once, when the pipeline is built.</description></item>
/// <item><description>The request comes from this machine; checked per request.</description></item>
/// </list>
/// </summary>
internal static class TestEndpointsGate
{
    /// <summary>The paths the test-only endpoints answer.</summary>
    public const string Prefix = "/jfb-testing";

    /// <summary>True when the host may answer the test-only paths at all.</summary>
    public static bool IsOpen(FormBuilderTestingOptions options, IHostEnvironment environment) =>
        options.Endpoints && environment.IsDevelopment();

    /// <summary>True for a path under <see cref="Prefix"/>.</summary>
    public static bool IsTestPath(PathString path) =>
        path.StartsWithSegments(Prefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the request comes from this machine: the browser tests run beside their host.</summary>
    public static bool IsLocal(HttpContext context) =>
        context.Connection.RemoteIpAddress is { } address
        && IPAddress.IsLoopback(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);
}

/// <summary>Adds <see cref="TestEndpointsMiddleware"/> in front of the host's pipeline when the gate is open.</summary>
internal sealed class TestEndpointsStartupFilter(IOptions<FormBuilderTestingOptions> options, IHostEnvironment environment) : IStartupFilter
{
    /// <inheritdoc />
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        if (TestEndpointsGate.IsOpen(options.Value, environment))
        {
            app.UseMiddleware<TestEndpointsMiddleware>();
        }

        next(app);
    };
}

/// <summary>
/// The test-only endpoints. Each later step adds what its browser tests need (seeding, running a job now, ageing
/// entries); these are the first:
/// <list type="bullet">
/// <item><description><c>GET /jfb-testing/status</c>: <c>{"testHost": true, "clock": ..., "utc": ..., "stub": ...}</c>,
/// so a global set-up can tell that it reached a test host.</description></item>
/// <item><description><c>/jfb-testing/clock</c>: reads (<c>GET</c>), moves (<c>POST {"utc": "..."}</c> or
/// <c>{"advanceSeconds": 360}</c>) and resets (<c>DELETE</c>) the host's <see cref="TestClock"/>; <c>404</c> when the
/// host has none. The browser specs share their host, so none of them moves it: a moved clock shifts every other spec's
/// times while it runs, an entry's undo window among them.</description></item>
/// <item><description><c>GET /jfb-testing/access</c>: each backoffice user's effective Jiron Form Builder permissions,
/// globally, at the root and on each root folder outside the trash in the library's order (ADR-0049), so the browser
/// tests can see that the personas have their grants.</description></item>
/// <item><description><c>GET /jfb-testing/entries?form=&lt;alias&gt;</c>: the form's entries, oldest first, each with its
/// state, the member it keeps (ADR-0231), its stored values by the field's key, row and item (ADR-0100) and its files by the field's key, name, size and the name
/// a typed signature holds (ADR-0102, ADR-0103), so the browser tests can compare what two paths stored for the same
/// answers; <c>404</c> for a form the host does not have.</description></item>
/// <item><description><c>POST /jfb-testing/protection {"captchaConsent": "cmp"}</c>: sets the protection settings' consent mode for
/// a captcha (ADR-0157), which no screen sets yet; <c>204</c>.</description></item>
/// <item><description><c>POST /jfb-testing/form-restriction {"form": "&lt;key&gt;", "restricted": true}</c>: gives a form its own
/// restriction, or takes it away (ADR-0013), which no screen sets yet, so the browser tests can show what the library says of a
/// restricted form (ADR-0166); <c>204</c>, or <c>404</c> for a form the host does not have.</description></item>
/// <item><description><c>POST /jfb-testing/sent-expired {"form": "&lt;alias&gt;", "value": "&lt;jfb-sent value&gt;"}</c>: the
/// same form's, instance's and entry's <c>jfb-sent</c> value, expired a minute ago (ADR-0123), so the browser tests see
/// what an expired one shows without moving the clock that every test shares; <c>404</c> for a form the host does not
/// have or a value that names nothing for it.</description></item>
/// <item><description><c>POST /jfb-testing/mail-layouts {"marker": "...", "word": "..."}</c>: queues a mail in every layout's shape, each
/// holding the word wherever text goes, with the subject <c>&lt;marker&gt; &lt;layout&gt;</c>, and answers those subjects, so the browser
/// tests can render what arrives in the pickup folder (ADR-0159).</description></item>
/// <item><description><c>POST /jfb-testing/verification-form {"url": "..."}</c>: publishes a headless form that asks for its address to be confirmed (ROADMAP 8.5).</description></item>
/// <item><description><c>POST /jfb-testing/webhook-form {"url": "..."}</c>: publishes a headless form whose webhook action calls this address (ROADMAP 8.4).</description></item>
/// <item><description><c>GET /jfb-testing/mail-log?key=...</c>: the states of the mail one mailbox got, by any spelling of it (ADR-0159).</description></item>
/// <item><description><c>GET /jfb-testing/mail-view?view=...</c>: what the site's view of a mail writes for a fixed model (ADR-0160).</description></item>
/// <item><description><c>POST /jfb-testing/entries-form {"count": 1000, "url": "..."}</c>: publishes a form whose entries wait
/// for approval, named by their name, with a position to choose, an amount, a sensitive salary and a webhook on approval to
/// this address, and sends it that many entries through the submission pipeline, as a server key would (ADR-0207); at most
/// 2,000. <c>"places"</c> gives the accountant's position that many places, <c>"unique"</c> makes the name unique per form,
/// <c>"notice"</c> adds a mail of each entry with its <c>{{ entry.url }}</c> to this address, <c>"forward"</c> makes this
/// address one entries may be forwarded to (ADR-0208), and <c>"spam"</c> moves that many of the first entries to Spam,
/// as a reviewer would.</description></item>
/// <item><description><c>POST /jfb-testing/insights-form {"name": "..."}</c>: publishes a headless form of two pages (Name and
/// Email, then Your message) and counts 60 days of visits to it in <c>jfbDailyStat</c> as its beacons and entries would, the
/// same numbers each time, so the Insights view and Forms at a glance show a month against the one before (ADR-0211).</description></item>
/// <item><description><c>POST /jfb-testing/answers-form {"name": "...", "count": 120}</c>: publishes a headless form with an answer
/// of every kind (one choice with Other, several, a ranking, a grid, a scale from 0 to 10, a number, a checkbox, a paragraph of text,
/// an email and a repeatable group) and sends it that many entries through the submission pipeline, the same answers each time;
/// the first twelve before an option of its first field is taken away (ADR-0212).</description></item>
/// <item><description><c>GET /jfb-testing/audit?entity=&lt;key&gt;</c>: the audit log's entries about one item, oldest first, each with
/// its action, user and details (ADR-0041), which no screen lists yet.</description></item>
/// </list>
/// Requests from another machine, and paths outside <c>/jfb-testing/</c>, go on to the site as if this did not exist.
/// </summary>
internal sealed class TestEndpointsMiddleware(RequestDelegate next)
{
    // A test host has a handful of users, and a form there a handful of entries.
    private const int MaxUsers = 200;
    private const int MaxEntries = 500;

    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;

    // The settings' consent mode for a captcha, kept with the rest of what is set.
    private static async Task ProtectionAsync(HttpContext context)
    {
        var body = await JsonSerializer.DeserializeAsync<ProtectionRequest>(context.Request.Body, Json, context.RequestAborted);
        var settings = context.RequestServices.GetRequiredService<IFormBuilderSettingsService>();
        var current = await settings.GetAsync<ProtectionSettings>(null, context.RequestAborted);
        var mode = Enum.Parse<CaptchaConsentMode>(body?.CaptchaConsent ?? nameof(CaptchaConsentMode.Explicit), ignoreCase: true);
        _ = await settings.SaveAsync(current.Value with { CaptchaConsent = mode }, current.Version, null, context.RequestAborted);
        context.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    private sealed record ProtectionRequest(string? CaptchaConsent);

    // A form's own restriction, set as the access service sets it, at the version the form has now.
    private static async Task FormRestrictionAsync(HttpContext context)
    {
        var body = await JsonSerializer.DeserializeAsync<FormRestrictionRequest>(context.Request.Body, Json, context.RequestAborted);
        var forms = context.RequestServices.GetRequiredService<IFormBuilderFormService>();
        if (body is null || await forms.GetAsync(body.Form, context.RequestAborted) is not { } form)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var access = context.RequestServices.GetRequiredService<IFormBuilderAccessService>();
        _ = await access.RestrictFormAsync(form.Key, form.ConcurrencyVersion, body.Restricted, null, context.RequestAborted);
        context.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    private sealed record FormRestrictionRequest(Guid Form, bool Restricted);

    // A published headless form with one field and a webhook action to this address, signed with the host's secret.
    private static async Task WebhookFormAsync(HttpContext context)
    {
        var body = await JsonSerializer.DeserializeAsync<WebhookFormRequest>(context.Request.Body, Json, context.RequestAborted);
        var forms = context.RequestServices.GetRequiredService<IFormBuilderFormService>();
        var created = await forms.CreateAsync(new NewFormRequest($"Webhook {Guid.NewGuid():N}"), context.RequestAborted);
        var draft = (await forms.GetDraftAsync(created.Key, cancellationToken: context.RequestAborted))!;
        var definition = draft.Definition!;
        var field = new FormField { Key = Guid.NewGuid(), Type = "text", Alias = "name", Label = new LocalizedText("Name") };
        var settings = JsonSerializer.Serialize(new { url = body?.Url, secret = "$JironFormBuilder:Secrets:E2EWebhook" });
        var saved = (await forms.SaveDraftAsync(
            created.Key,
            draft.Form.ConcurrencyVersion,
            definition with
            {
                Pages = [definition.Pages[0] with { Groups = [definition.Pages[0].Groups[0] with { Fields = [field] }] }],
                Settings = definition.Settings with { Developer = definition.Settings.Developer with { Headless = true } },
                Actions = [new FormAction { Key = Guid.NewGuid(), Type = "webhook", Name = "Hook", Settings = SettingsObject.Parse(settings) }],
            },
            null,
            null,
            context.RequestAborted))!;
        _ = await forms.PublishAsync(created.Key, saved.Form.ConcurrencyVersion, null, null, context.RequestAborted);
        await context.Response.WriteAsJsonAsync(new { key = created.Key }, Json, context.RequestAborted);
    }

    private sealed record WebhookFormRequest(string? Url);

    // A published headless form that asks for its address to be confirmed, with a webhook on submission and one on confirmation, each to its own
    // path below this address, on a site that knows its own address.
    private static async Task VerificationFormAsync(HttpContext context)
    {
        var body = await JsonSerializer.DeserializeAsync<WebhookFormRequest>(context.Request.Body, Json, context.RequestAborted);
        await KnowBaseUrlAsync(context);
        var forms = context.RequestServices.GetRequiredService<IFormBuilderFormService>();
        var created = await forms.CreateAsync(new NewFormRequest($"Opt-in {Guid.NewGuid():N}"), context.RequestAborted);
        var draft = (await forms.GetDraftAsync(created.Key, cancellationToken: context.RequestAborted))!;
        var definition = draft.Definition!;
        var name = new FormField { Key = Guid.NewGuid(), Type = "text", Alias = "name", Label = new LocalizedText("Name") };
        var email = new FormField { Key = Guid.NewGuid(), Type = "email", Alias = "email", Label = new LocalizedText("Email") };
        FormAction Hook(string path, ActionTrigger trigger) => new()
        {
            Key = Guid.NewGuid(),
            Type = "webhook",
            Name = path,
            Trigger = trigger,
            Settings = SettingsObject.Parse(JsonSerializer.Serialize(new { url = $"{body?.Url}/{path}", secret = "$JironFormBuilder:Secrets:E2EWebhook" })),
        };
        var saved = (await forms.SaveDraftAsync(
            created.Key,
            draft.Form.ConcurrencyVersion,
            definition with
            {
                Pages = [definition.Pages[0] with { Groups = [definition.Pages[0].Groups[0] with { Fields = [name, email] }] }],
                Settings = definition.Settings with
                {
                    Developer = definition.Settings.Developer with { Headless = true },
                    Submission = definition.Settings.Submission with { Verification = new EmailVerification { EmailField = email.Key, Days = 7 } },
                },
                Actions = [Hook("submitted", ActionTrigger.Submitted), Hook("verified", ActionTrigger.Verified)],
            },
            null,
            null,
            context.RequestAborted))!;
        _ = await forms.PublishAsync(created.Key, saved.Form.ConcurrencyVersion, null, null, context.RequestAborted);
        await context.Response.WriteAsJsonAsync(new { key = created.Key }, Json, context.RequestAborted);
    }

    // One mail in every shape a mail can take, each holding this word wherever text goes, through the queue.
    private static async Task MailLayoutsAsync(HttpContext context)
    {
        var body = await JsonSerializer.DeserializeAsync<MailLayoutsRequest>(context.Request.Body, Json, context.RequestAborted);
        var marker = body?.Marker ?? Guid.NewGuid().ToString("N");
        var word = body?.Word ?? new string('x', 300);
        var services = context.RequestServices;
        var outbox = services.GetRequiredService<EmailOutbox>();
        var layouts = services.GetRequiredService<IEmailLayoutRepository>();
        var keys = new Dictionary<string, Guid> { ["card"] = EmailLayouts.CardKey, ["letter"] = EmailLayouts.LetterKey };
        foreach (var look in new[] { "card", "letter" })
        {
            var design = JsonSerializer.Serialize(new { look, header = new Dictionary<string, string> { [""] = $"<p>{word}</p>" }, footer = new Dictionary<string, string> { [""] = $"<p>{word}</p>" } }, Json);
            keys[$"dressed-{look}"] = (await layouts.CreateAsync(new NewEmailLayout(Guid.NewGuid(), $"{marker}-{look}", $"{marker} {look}", design), context.RequestAborted)).Key;
        }

        var html = $"<h2>{word}</h2><p>{word} <a href=\"https://jfb.test/{word}\">{word}</a></p><ul><li>{word}</li></ul><blockquote>{word}</blockquote><pre>{word}</pre><p><code>{word}</code></p><table><tr><th>{word}</th><td>{word}</td></tr></table>";
        foreach (var (name, key) in keys)
        {
            await outbox.QueueAsync(new OutgoingEmail { Kind = EmailKind.Test, To = ["layouts@jfb.test"], Subject = $"{marker} {name}", Body = html, Culture = "en", Layout = key }, context.RequestAborted);
        }

        await context.Response.WriteAsJsonAsync(new { subjects = keys.Keys.Select(name => $"{marker} {name}") }, Json, context.RequestAborted);
    }

    private sealed record MailLayoutsRequest(string? Marker, string? Word);

    // A form whose entries wait for approval, and its entries, each sent as the public API sends a server key's.
    private static async Task EntriesFormAsync(HttpContext context)
    {
        var body = await JsonSerializer.DeserializeAsync<EntriesFormRequest>(context.Request.Body, Json, context.RequestAborted);
        var count = Math.Clamp(body?.Count ?? 0, 0, 2000);
        var forms = context.RequestServices.GetRequiredService<IFormBuilderFormService>();
        var created = await forms.CreateAsync(new NewFormRequest(body?.Name ?? $"Applications {Guid.NewGuid():N}"), context.RequestAborted);
        var draft = (await forms.GetDraftAsync(created.Key, cancellationToken: context.RequestAborted))!;
        var definition = draft.Definition!;
        string[] positions = ["Frontend developer", "Backend developer", "Accountant", "Office manager"];
        var name = new FormField
        {
            Key = Guid.NewGuid(),
            Type = "text",
            Alias = "name",
            Label = new LocalizedText("Name"),
            Validation = body?.Unique == true ? [new UniquePerFormCheck()] : [],
        };

        // The accountant's position, the third, holds as many places as asked, so a move back onto it can find it full (ADR-0090).
        var position = new FormField
        {
            Key = Guid.NewGuid(),
            Type = "dropdown",
            Alias = "position",
            Label = new LocalizedText("Position"),
            Options = new FieldOptions
            {
                Items = [.. positions.Select((label, index) => new FieldOption { Value = $"p{index + 1}", Label = new LocalizedText(label), Places = index == 2 ? body?.Places : null })],
            },
        };
        var amount = new FormField { Key = Guid.NewGuid(), Type = "number", Alias = "amount", Label = new LocalizedText("Amount") };
        var salary = new FormField { Key = Guid.NewGuid(), Type = "text", Alias = "salary", Label = new LocalizedText("Salary"), Sensitive = true };
        var hook = new FormAction
        {
            Key = Guid.NewGuid(),
            Type = "webhook",
            Name = "Tell the team",
            Trigger = ActionTrigger.Approved,
            Settings = SettingsObject.Parse(JsonSerializer.Serialize(new { url = body?.Url ?? "http://127.0.0.1:5560/approved", secret = "$JironFormBuilder:Secrets:E2EWebhook" })),
        };
        List<FormAction> actions = [hook];
        if (body?.Notice is { Length: > 0 } notice)
        {
            // A notice of each entry with its own link, on a site that knows its address, as the team gets one.
            await KnowBaseUrlAsync(context);
            var texts = new Dictionary<string, object>
            {
                ["to"] = new[] { notice },
                ["subject"] = new Dictionary<string, string> { [""] = "Entry {{ entry.number }}" },
                // A merge tag never goes inside markup (ADR-0064), so the link is the text itself.
                ["body"] = new Dictionary<string, string> { [""] = "<p>Open entry {{ entry.number }}: {{ entry.url }}</p>" },
            };
            actions.Add(new FormAction { Key = Guid.NewGuid(), Type = EmailActionType.Alias, Name = "Notice", Settings = SettingsObject.Parse(JsonSerializer.Serialize(texts)) });
        }

        if (body?.Forward is { Length: > 0 } forward)
        {
            await KnowBaseUrlAsync(context);
            var settingsService = context.RequestServices.GetRequiredService<IFormBuilderSettingsService>();
            var email = await settingsService.GetAsync<EmailSettings>(null, context.RequestAborted);
            if (!email.Value.ForwardAddresses.Contains(forward, StringComparer.OrdinalIgnoreCase))
            {
                _ = await settingsService.SaveAsync(email.Value with { ForwardAddresses = [.. email.Value.ForwardAddresses, forward] }, email.Version, null, context.RequestAborted);
            }
        }

        var saved = (await forms.SaveDraftAsync(
            created.Key,
            draft.Form.ConcurrencyVersion,
            definition with
            {
                Pages = [definition.Pages[0] with { Groups = [definition.Pages[0].Groups[0] with { Fields = [name, position, amount, salary] }] }],
                Settings = definition.Settings with
                {
                    Developer = definition.Settings.Developer with { Headless = true },
                    Submission = definition.Settings.Submission with { Approval = ApprovalMode.Manual, EntryTitle = "{{ field.name }}" },
                },
                Actions = actions,
            },
            null,
            null,
            context.RequestAborted))!;
        _ = await forms.PublishAsync(created.Key, saved.Form.ConcurrencyVersion, null, null, context.RequestAborted);
        var published = (await forms.GetAsync(created.Key, context.RequestAborted))!;
        var submissions = context.RequestServices.GetRequiredService<FormSubmissions>();
        for (var i = 1; i <= count; i++)
        {
            var result = await submissions.SubmitAsync(
                new SubmissionRequest
                {
                    Form = created.Key,
                    Revision = published.PublicRevision,
                    SubmissionId = Guid.NewGuid(),
                    Culture = "en-US",
                    ServerKey = "e2e-seed",
                    Fields = new Dictionary<string, PostedAnswer>
                    {
                        ["name"] = new([$"Person {i:0000}"]),
                        ["position"] = new([$"p{(i % positions.Length) + 1}"]),
                        ["amount"] = new([(i % 500).ToString(System.Globalization.CultureInfo.InvariantCulture)]),
                        ["salary"] = new([$"{30000 + i}"]),
                    },
                },
                context.RequestAborted);
            if (result.Receipt is null)
            {
                throw new InvalidOperationException($"The test host could not send entry {i}: {result.Refusal}.");
            }
        }

        if (Math.Clamp(body?.Spam ?? 0, 0, count) is > 0 and var spam)
        {
            var entries = context.RequestServices.GetRequiredService<IEntryRepository>();
            var row = (await context.RequestServices.GetRequiredService<IFormRepository>().GetAsync(created.Key, context.RequestAborted))!;
            var keys = new List<Guid>();
            foreach (var id in (await entries.ListIdsAsync(row.Id, count, context.RequestAborted)).Take(spam))
            {
                keys.Add((await entries.GetByIdAsync(id, context.RequestAborted))!.Key);
            }

            _ = await context.RequestServices.GetRequiredService<EntryStateChanges>().ChangeAsync(created.Key, keys, EntryState.Spam, userKey: null, cancellationToken: context.RequestAborted);
        }

        await context.Response.WriteAsJsonAsync(new { key = created.Key, alias = published.Alias, positions }, Json, context.RequestAborted);
    }

    private sealed record EntriesFormRequest(int? Count, string? Url, string? Name, int? Places = null, bool? Unique = null, string? Notice = null, string? Forward = null, int? Spam = null);

    private static async Task InsightsFormAsync(HttpContext context)
    {
        var body = await JsonSerializer.DeserializeAsync<EntriesFormRequest>(context.Request.Body, Json, context.RequestAborted);
        var forms = context.RequestServices.GetRequiredService<IFormBuilderFormService>();
        var created = await forms.CreateAsync(new NewFormRequest(body?.Name ?? $"Insights {Guid.NewGuid():N}"), context.RequestAborted);
        var draft = (await forms.GetDraftAsync(created.Key, cancellationToken: context.RequestAborted))!;
        var definition = draft.Definition!;
        var name = new FormField { Key = Guid.NewGuid(), Type = "text", Alias = "name", Label = new LocalizedText("Name"), Required = true };
        var email = new FormField { Key = Guid.NewGuid(), Type = "email", Alias = "email", Label = new LocalizedText("Email address"), Required = true };
        var phone = new FormField { Key = Guid.NewGuid(), Type = "text", Alias = "phone", Label = new LocalizedText("Phone") };
        var message = new FormField { Key = Guid.NewGuid(), Type = "textarea", Alias = "message", Label = new LocalizedText("Your message"), Required = true };
        var second = new FormPage { Key = Guid.NewGuid(), Title = new LocalizedText("Your message"), Groups = [new FormGroup { Key = Guid.NewGuid(), Fields = [message] }] };
        var saved = (await forms.SaveDraftAsync(
            created.Key,
            draft.Form.ConcurrencyVersion,
            definition with
            {
                Pages = [definition.Pages[0] with { Title = new LocalizedText("Your details"), Groups = [definition.Pages[0].Groups[0] with { Fields = [name, email, phone] }] }, second],
                Settings = definition.Settings with { Developer = definition.Settings.Developer with { Headless = true } },
            },
            null,
            null,
            context.RequestAborted))!;
        _ = await forms.PublishAsync(created.Key, saved.Form.ConcurrencyVersion, null, null, context.RequestAborted);

        // 60 days, the same each time: a weekday sees more visits than a weekend, and the last 30 days a little more.
        var row = (await context.RequestServices.GetRequiredService<IFormRepository>().GetAsync(created.Key, context.RequestAborted))!;
        var stats = context.RequestServices.GetRequiredService<IDailyStatRepository>();
        var today = DateOnly.FromDateTime(context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime);
        var pages = context.RequestServices.GetRequiredService<global::Umbraco.Cms.Core.Services.Navigation.IDocumentNavigationQueryService>()
            .TryGetRootKeys(out var roots) ? roots.Take(3).ToList() : [];
        string Key(Guid key) => key.ToString("N");
        for (var back = 59; back >= 0; back--)
        {
            var day = today.AddDays(-back);
            var weekend = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            var wave = (int)(Math.Sin(back * 0.9) * 12);
            var views = (weekend ? 70 : 150) + wave + (back < 30 ? 14 : 0);
            var starts = views * 27 / 100;
            var reached = starts * 46 / 100;
            var sent = reached * 64 / 100;
            List<DailyStatCount> counts =
            [
                new("views", By: views),
                new("starts", By: starts),
                new("page", Key(second.Key), reached),
                new("submissions", By: sent),
                new("spam", By: back % 4 == 0 ? 1 : 0),
                new("invalid", $"{Key(email.Key)}:invalid", starts * 16 / 100),
                new("invalid", $"{Key(email.Key)}:required", starts * 3 / 100),
                new("invalid", $"{Key(phone.Key)}:minLength", starts * 5 / 100),
                new("invalid", $"{Key(message.Key)}:required", starts * 2 / 100),
                new("completion", "60", sent / 5),
                new("completion", "120", sent / 4),
                new("completion", "180", sent / 4),
                new("completion", "300", sent / 6),
                new("completion", "more", sent / 10),
            ];
            for (var hour = 7; hour <= 21; hour++)
            {
                counts.Add(new("hour", hour.ToString(System.Globalization.CultureInfo.InvariantCulture), Math.Max(0, (sent / 6) - Math.Abs(hour - 11) / 2)));
            }

            counts.AddRange(pages.Select((page, index) => new DailyStatCount("origin", Key(page), sent / (index + 2))));
            await stats.IncrementOnAsync(row.Id, day, counts, context.RequestAborted);
        }

        await context.Response.WriteAsJsonAsync(new { key = created.Key }, Json, context.RequestAborted);
    }

    // The comments of the answers form's entries, one in four of them; the last runs past two lines.
    private static readonly string[] Comments =
    [
        "The workshop on accessible forms was the highlight of my day.",
        "Please offer more vegetarian dishes at lunch.",
        "The venue was hard to find from the station; a sign at the exit would help.",
        "Great speakers, but the afternoon sessions ran late.",
        "I would love the slides to be shared afterwards.",
        "The wireless network dropped out during the opening talk.",
        "Loved the networking hour: I met three people I will work with.",
        "Seats at the back could not see the screen well.",
        "More time for questions after each talk, please.",
        "The panel on data protection answered exactly the questions our team had been asking for months, and I came away with a clear plan for our own forms, which is more than I expected from a single afternoon.",
    ];

    private static async Task AnswersFormAsync(HttpContext context)
    {
        var body = await JsonSerializer.DeserializeAsync<EntriesFormRequest>(context.Request.Body, Json, context.RequestAborted);
        var count = Math.Clamp(body?.Count ?? 120, 12, 2000);
        var forms = context.RequestServices.GetRequiredService<IFormBuilderFormService>();
        var submissions = context.RequestServices.GetRequiredService<FormSubmissions>();
        var created = await forms.CreateAsync(new NewFormRequest(body?.Name ?? $"Event feedback {Guid.NewGuid():N}"), context.RequestAborted);
        static FieldOption Option(string value, string label) => new() { Value = value, Label = new LocalizedText(label) };
        static FormField Field(string type, string alias, string label) => new() { Key = Guid.NewGuid(), Type = type, Alias = alias, Label = new LocalizedText(label) };
        FieldOption[] ways = [Option("search", "Search engine"), Option("friend", "A friend"), Option("social", "Social media"), Option("newsletter", "Our newsletter")];
        var heard = Field("radio", "heard", "How did you hear about us?") with
        {
            Settings = SettingsObject.Parse("""{"other":true}"""),
            Options = new FieldOptions { Items = [.. ways, Option("radio", "Radio advert")] },
        };
        var sessions = Field("checkboxes", "sessions", "Which sessions did you join?") with
        {
            Options = new FieldOptions { Items = [Option("opening", "Opening talk"), Option("workshops", "Workshops"), Option("panel", "Panel"), Option("networking", "Networking hour")] },
        };
        var matters = Field("ranking", "matters", "Rank what matters most to you") with
        {
            Options = new FieldOptions { Items = [Option("content", "Content"), Option("speakers", "Speakers"), Option("venue", "Venue"), Option("price", "Price")] },
        };
        GridRow[] parts = [new() { Key = Guid.NewGuid(), Label = new LocalizedText("Talks") }, new() { Key = Guid.NewGuid(), Label = new LocalizedText("Organisation") }, new() { Key = Guid.NewGuid(), Label = new LocalizedText("Food") }];
        var day = Field("grid", "day", "How was the day?") with
        {
            Options = new FieldOptions { Items = [Option("poor", "Poor"), Option("fair", "Fair"), Option("good", "Good"), Option("excellent", "Excellent")] },
            Rows = parts,
        };
        var recommend = Field("scale", "recommend", "How likely are you to recommend us to a friend?");
        var age = Field("number", "age", "Your age");
        var newsletter = Field("checkbox", "newsletter", "Send me the newsletter");
        var comments = Field("textarea", "comments", "Anything else you would like to tell us?");
        var email = Field("email", "email", "Email address");
        var meal = Field("radio", "meal", "Meal") with
        {
            Options = new FieldOptions { Items = [Option("veggie", "Vegetarian"), Option("fish", "Fish"), Option("meat", "Meat")] },
        };
        var guests = Field("repeater", "guests", "Guests") with { Fields = [meal] };

        async Task<int> PublishAsync(FormField first)
        {
            var draft = (await forms.GetDraftAsync(created.Key, cancellationToken: context.RequestAborted))!;
            var definition = draft.Definition!;
            var saved = (await forms.SaveDraftAsync(
                created.Key,
                draft.Form.ConcurrencyVersion,
                definition with
                {
                    Pages = [definition.Pages[0] with { Groups = [definition.Pages[0].Groups[0] with { Fields = [first, sessions, matters, day, recommend, age, newsletter, comments, email, guests] }] }],
                    Settings = definition.Settings with { Developer = definition.Settings.Developer with { Headless = true } },
                },
                null,
                null,
                context.RequestAborted))!;
            _ = await forms.PublishAsync(created.Key, saved.Form.ConcurrencyVersion, null, null, context.RequestAborted);
            return (await forms.GetAsync(created.Key, context.RequestAborted))!.PublicRevision;
        }

        static PostedAnswer One(string text) => new([text]);
        string[] orders = ["content speakers venue price", "speakers content price venue", "content price speakers venue", "venue content speakers price", "content speakers price venue"];
        string[] columns = ["excellent", "good", "good", "fair", "good", "poor"];
        int[] scores = [10, 9, 8, 9, 7, 10, 6, 9, 4, 8, 10, 9];
        string[] cycle = ["search", "friend", "search", "social", "friend", "search", "newsletter"];
        var revision = await PublishAsync(heard);
        for (var i = 1; i <= count; i++)
        {
            if (i == 13)
            {
                // The radio advert is no longer offered: the entries before keep it, under their field.
                revision = await PublishAsync(heard with { Options = new FieldOptions { Items = ways } });
            }

            var answers = new Dictionary<string, PostedAnswer>(StringComparer.Ordinal)
            {
                ["heard"] = i % 11 == 0
                    ? new PostedAnswer([], new Dictionary<string, IReadOnlyList<string?>>(StringComparer.Ordinal) { ["other"] = ["A colleague told me"] })
                    : One(i <= 12 && i % 3 == 0 ? "radio" : cycle[i % cycle.Length]),
                ["sessions"] = new([.. new[] { i % 3 != 0 ? "opening" : null, i % 2 == 0 ? "workshops" : null, i % 5 == 0 ? "panel" : null, i % 4 == 1 ? "networking" : null }.Where(value => value is not null)]),
                ["matters"] = new([.. orders[i % orders.Length].Split(' ')]),
                ["day"] = new([], new Dictionary<string, IReadOnlyList<string?>>(StringComparer.Ordinal)
                {
                    [parts[0].Key.ToString("D")] = [columns[i % columns.Length]],
                    [parts[1].Key.ToString("D")] = [columns[(i + 2) % columns.Length]],
                    [parts[2].Key.ToString("D")] = [i % 6 == 0 ? null : columns[(i + 4) % columns.Length]],
                }),
                ["recommend"] = One(scores[i % scores.Length].ToString(System.Globalization.CultureInfo.InvariantCulture)),
            };
            if (i % 9 != 0)
            {
                answers["age"] = One((19 + (i * 7 % 48)).ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            if (i % 3 == 0)
            {
                answers["newsletter"] = One("true");
            }

            if (i % 4 == 0)
            {
                answers["comments"] = One(Comments[i / 4 % Comments.Length]);
            }

            if (i % 5 != 0)
            {
                answers["email"] = One($"guest{i}@visitor.jfb.test");
            }

            IReadOnlyDictionary<string, PostedAnswer> Row(string choice) => new Dictionary<string, PostedAnswer>(StringComparer.Ordinal) { ["meal"] = One(choice) };
            IReadOnlyList<IReadOnlyDictionary<string, PostedAnswer>> rows = (i % 4) switch
            {
                1 => [Row("veggie"), Row("fish")],
                2 => [Row("meat")],
                _ => [],
            };
            var result = await submissions.SubmitAsync(
                new SubmissionRequest
                {
                    Form = created.Key,
                    Revision = revision,
                    SubmissionId = Guid.NewGuid(),
                    Culture = "en-US",
                    ServerKey = "e2e-seed",
                    Fields = answers,
                    Rows = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, PostedAnswer>>>(StringComparer.Ordinal) { ["guests"] = rows },
                },
                context.RequestAborted);
            if (result.Receipt is null)
            {
                throw new InvalidOperationException($"The test host could not send entry {i}: {result.Refusal}.");
            }
        }

        await context.Response.WriteAsJsonAsync(new { key = created.Key }, Json, context.RequestAborted);
    }

    // The site's own address, from this request, unless the settings name one, so links in mails lead back here.
    private static async Task KnowBaseUrlAsync(HttpContext context)
    {
        var settingsService = context.RequestServices.GetRequiredService<IFormBuilderSettingsService>();
        var general = await settingsService.GetAsync<GeneralSettings>(null, context.RequestAborted);
        if (general.Value.BaseUrl is null)
        {
            _ = await settingsService.SaveAsync(general.Value with { BaseUrl = $"{context.Request.Scheme}://{context.Request.Host}" }, general.Version, null, context.RequestAborted);
        }
    }

    // What the audit log holds of one item, oldest first, with the details as stored.
    private static async Task AuditAsync(HttpContext context)
    {
        if (!Guid.TryParse(context.Request.Query["entity"], out var entity))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var audit = context.RequestServices.GetRequiredService<IAuditRepository>();
        var page = await audit.QueryAsync(new AuditQuery { EntityKey = entity }, PageRequest.First(PageRequest.MaxTake), context.RequestAborted);
        var entries = page.Items
            .OrderBy(item => item.Id)
            .Select(item => new { action = item.Action, user = item.UserKey, details = JsonDocument.Parse(item.Details).RootElement });
        await context.Response.WriteAsJsonAsync(new { entries }, Json, context.RequestAborted);
    }

    // A form whose entry has one of each problem the deliveries log shows (ROADMAP 12.4b, ADR-0219): an email action's run
    // whose mail failed and whose other mail is uncertain, a mail to a visitor that waits and one the limit held back, and an
    // uncertain webhook run; the form has the name asked for, so a journey finds its own rows. Nothing is sent: the run and
    // the mail are written as the outbox leaves them.
    private static async Task DeliveriesAsync(HttpContext context)
    {
        var body = await JsonSerializer.DeserializeAsync<DeliveriesRequest>(context.Request.Body, Json, context.RequestAborted);
        var services = context.RequestServices;
        var forms = services.GetRequiredService<IFormBuilderFormService>();
        var runs = services.GetRequiredService<IActionRunRepository>();
        var mail = services.GetRequiredService<IEmailRepository>();
        var created = await forms.CreateAsync(new NewFormRequest(body?.Name ?? $"Deliveries {Guid.NewGuid():N}"), context.RequestAborted);
        var draft = (await forms.GetDraftAsync(created.Key, cancellationToken: context.RequestAborted))!;
        var definition = draft.Definition!;
        var field = new FormField { Key = Guid.NewGuid(), Type = "text", Alias = "name", Label = new LocalizedText("Name") };
        var email = new FormAction { Key = Guid.NewGuid(), Type = EmailActionType.Alias, Name = "Email the team" };
        var webhook = new FormAction
        {
            Key = Guid.NewGuid(),
            Type = "webhook",
            Name = "Send to the CRM",
            Settings = SettingsObject.Parse("""{"target":"url","url":"https://crm.example.com/hook"}"""),
        };
        _ = (await forms.SaveDraftAsync(
            created.Key,
            draft.Form.ConcurrencyVersion,
            definition with
            {
                Pages = [definition.Pages[0] with { Groups = [definition.Pages[0].Groups[0] with { Fields = [field] }] }],
                Actions = [email, webhook],
            },
            null,
            null,
            context.RequestAborted))!;
        var row = (await services.GetRequiredService<IFormRepository>().GetAsync(created.Key, context.RequestAborted))!;
        var entry = (await services.GetRequiredService<IEntryRepository>().CreateAsync(
            new NewEntry
            {
                Key = Guid.NewGuid(),
                FormId = row.Id,
                FormVersionId = row.DraftVersionId!.Value,
                State = EntryState.Submitted,
                SubmissionId = Guid.NewGuid(),
                SubmissionHash = new string('a', 64),
                Culture = "en-US",
            },
            context.RequestAborted))!;
        var queued = (await runs.AddAsync(
            entry.Id,
            entry.FormVersionId,
            Guid.NewGuid(),
            [new NewActionRun(email.Key, email.Type, ActionTrigger.Submitted, 0, false), new NewActionRun(webhook.Key, webhook.Type, ActionTrigger.Submitted, 1, false)],
            cancellationToken: context.RequestAborted))!;
        var lease = TimeSpan.FromMinutes(5);
        await runs.SucceedAsync(queued[0].Id, (await runs.ClaimAsync(queued[0].Id, "e2e", lease, context.RequestAborted))!.LockedBy!, context.RequestAborted);
        await runs.CutOffAsync(queued[1].Id, (await runs.ClaimAsync(queued[1].Id, "e2e", lease, context.RequestAborted))!.LockedBy!, new DeliveryError("attempt_timed_out"), context.RequestAborted);

        NewEmail Team(string to) => new(EmailKind.Action, to, string.Empty, "Subject", "Text", "en", EntryId: entry.Id, ActionRunId: queued[0].Id);
        var keep = TimeSpan.FromDays(30);
        var failed = (await mail.AddAsync(Team("team@example.com"), context.RequestAborted))!;
        await mail.FailAsync(failed.Id, (await mail.ClaimAsync(failed.Id, "e2e", lease, context.RequestAborted))!.LockedBy!, new DeliveryError("exception", "System.Net.Mail.SmtpException"), keep, context.RequestAborted);
        var visitor = $"visitor-{Guid.NewGuid():N}@example.com";
        var limits = new EmailLimits(1, 1_000_000, keep);
        _ = await mail.AddWithinLimitsAsync(Team(visitor) with { RecipientKey = visitor }, limits, context.RequestAborted);
        var limited = (await mail.AddWithinLimitsAsync(Team(visitor) with { RecipientKey = visitor }, limits, context.RequestAborted))!;
        await context.Response.WriteAsJsonAsync(
            new { key = created.Key, entry = entry.Key, webhookRun = queued[1].Id, failedMail = failed.Id, limitedMail = limited.Id, visitor },
            Json,
            context.RequestAborted);
    }

    private sealed record DeliveriesRequest(string? Name);

    // The states of the mail one mailbox got, as the deliveries log has them, by any spelling of it.
    private static async Task MailLogAsync(HttpContext context)
    {
        var key = EmailAddresses.KeyOf(context.Request.Query["key"].ToString());
        var database = context.RequestServices.GetRequiredService<FormBuilderDatabase>();
        var states = await database.ReadAsync(
            db => db.FetchListAsync<string>("SELECT state FROM jfbEmail WHERE recipientKey = @0", key),
            context.RequestAborted);
        await context.Response.WriteAsJsonAsync(new { states }, Json, context.RequestAborted);
    }

    // A site's view of a mail, written on no request of its own, as the worker writes it.
    private static async Task MailViewAsync(HttpContext context)
    {
        var views = context.RequestServices.GetRequiredService<IEnumerable<IFormBuilderEmailView>>();
        var model = new EmailViewModel { Subject = "S", Culture = "en", FormName = "<Contact>", EntryNumber = 7, Answers = "Name: <Ada>" };
        var written = await Task.Run(() => views.First().RenderAsync(context.Request.Query["view"].ToString(), model, CancellationToken.None));
        await context.Response.WriteAsJsonAsync(new { written }, Json, context.RequestAborted);
    }

    /// <summary>Handles one request.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        if (!TestEndpointsGate.IsTestPath(context.Request.Path) || !TestEndpointsGate.IsLocal(context))
        {
            await next(context);
            return;
        }

        var rest = context.Request.Path.Value![TestEndpointsGate.Prefix.Length..].Trim('/').ToLowerInvariant();
        switch (rest)
        {
            case "status" when HttpMethods.IsGet(context.Request.Method):
                await StatusAsync(context);
                return;
            case "clock":
                await ClockAsync(context);
                return;
            case "access" when HttpMethods.IsGet(context.Request.Method):
                await AccessAsync(context);
                return;
            case "entries" when HttpMethods.IsGet(context.Request.Method):
                await EntriesAsync(context);
                return;
            case "protection" when HttpMethods.IsPost(context.Request.Method):
                await ProtectionAsync(context);
                return;
            case "form-restriction" when HttpMethods.IsPost(context.Request.Method):
                await FormRestrictionAsync(context);
                return;
            case "sent-expired" when HttpMethods.IsPost(context.Request.Method):
                await SentExpiredAsync(context);
                return;
            case "mail-log" when HttpMethods.IsGet(context.Request.Method):
                await MailLogAsync(context);
                return;
            case "mail-view" when HttpMethods.IsGet(context.Request.Method):
                await MailViewAsync(context);
                return;
            case "verification-form" when HttpMethods.IsPost(context.Request.Method):
                await VerificationFormAsync(context);
                return;
            case "webhook-form" when HttpMethods.IsPost(context.Request.Method):
                await WebhookFormAsync(context);
                return;
            case "mail-layouts" when HttpMethods.IsPost(context.Request.Method):
                await MailLayoutsAsync(context);
                return;
            case "entries-form" when HttpMethods.IsPost(context.Request.Method):
                await EntriesFormAsync(context);
                return;
            case "insights-form" when HttpMethods.IsPost(context.Request.Method):
                await InsightsFormAsync(context);
                return;
            case "answers-form" when HttpMethods.IsPost(context.Request.Method):
                await AnswersFormAsync(context);
                return;
            case "deliveries" when HttpMethods.IsPost(context.Request.Method):
                await DeliveriesAsync(context);
                return;
            case "audit" when HttpMethods.IsGet(context.Request.Method):
                await AuditAsync(context);
                return;
            default:
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
        }
    }

    private static async Task StatusAsync(HttpContext context)
    {
        var services = context.RequestServices;
        var time = services.GetService<TimeProvider>() ?? TimeProvider.System;
        var stub = services.GetService<StubHostedService>()?.Server?.BaseAddress;
        await context.Response.WriteAsJsonAsync(
            new { testHost = true, clock = services.GetService<TestClock>() is not null, utc = time.GetUtcNow(), stub },
            Json,
            context.RequestAborted);
    }

    private static async Task ClockAsync(HttpContext context)
    {
        if (context.RequestServices.GetService<TestClock>() is not { } clock)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (HttpMethods.IsDelete(context.Request.Method))
        {
            clock.Reset();
        }
        else if (HttpMethods.IsPost(context.Request.Method))
        {
            var request = await JsonSerializer.DeserializeAsync<ClockRequest>(context.Request.Body, Json, context.RequestAborted) ?? new ClockRequest();
            if (request.Utc is { } utc)
            {
                clock.SetNow(utc);
            }

            if (request.AdvanceSeconds is > 0 and var seconds)
            {
                clock.Advance(TimeSpan.FromSeconds(seconds));
            }
        }
        else if (!HttpMethods.IsGet(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return;
        }

        await context.Response.WriteAsJsonAsync(new { utc = clock.GetUtcNow(), offsetSeconds = clock.Offset.TotalSeconds }, Json, context.RequestAborted);
    }

    // The evaluation itself, as the management API's policies ask it, for every user and root folder of the host.
    private static async Task AccessAsync(HttpContext context)
    {
        var services = context.RequestServices;
        var access = services.GetRequiredService<IFormBuilderAccessService>();
        var folders = (await services.GetRequiredService<IFormBuilderFolderService>().ListAsync(cancellationToken: context.RequestAborted))
            .Where(folder => folder is { ParentKey: null, TrashedUtc: null })
            .ToList();
        var users = await services.GetRequiredService<IUserService>().GetAllAsync(Constants.Security.SuperUserKey, 0, MaxUsers);
        if (!users.Success || users.Result is not { } page)
        {
            throw new InvalidOperationException($"The test host could not read its users: {users.Status}.");
        }

        var answer = new List<object>();
        foreach (var user in page.Items.OrderBy(user => user.Email, StringComparer.OrdinalIgnoreCase))
        {
            var who = AccessUser.Of(user);
            var inFolders = new List<object>();
            foreach (var folder in folders)
            {
                inFolders.Add(new
                {
                    name = folder.Name,
                    restricted = folder.IsRestricted,
                    permissions = PermissionRules.NamesOf(await access.GetForFolderAsync(who, folder.Key, context.RequestAborted)),
                });
            }

            answer.Add(new
            {
                email = user.Email,
                section = who.HasSection,
                global = PermissionRules.NamesOf(await access.GetGlobalAsync(who, context.RequestAborted)),
                root = PermissionRules.NamesOf(await access.GetForFolderAsync(who, null, context.RequestAborted)),
                folders = inFolders,
            });
        }

        await context.Response.WriteAsJsonAsync(new { users = answer }, Json, context.RequestAborted);
    }

    // A form's entries with their stored values, as the store holds them.
    private static async Task EntriesAsync(HttpContext context)
    {
        var services = context.RequestServices;
        var forms = services.GetRequiredService<IFormRepository>();
        if (await forms.GetByAliasAsync(context.Request.Query["form"].ToString(), context.RequestAborted) is not { } form)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var entries = services.GetRequiredService<IEntryRepository>();
        var fields = (await forms.ListFieldsAsync(form.Id, context.RequestAborted)).ToDictionary(field => field.Id, field => field.Values.FieldKey);
        var ids = await entries.ListIdsAsync(form.Id, MaxEntries, context.RequestAborted);
        var values = ids.Count == 0 ? [] : await entries.ListValuesAsync(ids, context.RequestAborted);
        var files = ids.Count == 0 ? [] : await entries.ListFilesAsync(ids, context.RequestAborted);
        var store = services.GetRequiredService<IFormBuilderFileStore>();
        var typed = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            typed[file.StoredName] = await TypedNameAsync(store, file.StoredName, context.RequestAborted);
        }

        var stored = new Dictionary<long, (string? State, Guid? Member)>();
        foreach (var id in ids)
        {
            var entry = await entries.GetByIdAsync(id, context.RequestAborted);
            stored[id] = (entry?.State.ToString(), entry?.MemberKey);
        }

        var answer = ids.Order().Select(id => new
        {
            id,
            state = stored[id].State,
            member = stored[id].Member,
            values = values
                .Where(value => value.EntryId == id)
                .OrderBy(value => value.FieldId).ThenBy(value => value.RowIndex).ThenBy(value => value.ValueIndex)
                .Select(value => new { field = fields[value.FieldId], row = value.RowIndex, index = value.ValueIndex, value.Text, value.TextKey, value.Number, value.Date, value.Bool }),
            files = files
                .Where(file => file.EntryId == id)
                .OrderBy(file => file.Id)
                .Select(file => new { field = file.FieldKey, name = file.OriginalName, size = file.SizeBytes, typed = typed[file.StoredName] }),
        });
        await context.Response.WriteAsJsonAsync(new { entries = answer }, Json, context.RequestAborted);
    }

    // A form's sent value again, for the same instance and entry, expired a minute ago by the host's clock.
    private static async Task SentExpiredAsync(HttpContext context)
    {
        var services = context.RequestServices;
        var request = await JsonSerializer.DeserializeAsync<SentExpiredRequest>(context.Request.Body, Json, context.RequestAborted);
        var tokens = services.GetRequiredService<SentTokens>();
        var dot = request?.Value?.IndexOf('.', StringComparison.Ordinal) ?? -1;
        if (request is not { Form: { } alias, Value: { } value } || dot < 1
            || await services.GetRequiredService<IFormRepository>().GetByAliasAsync(alias, context.RequestAborted) is not { } form
            || tokens.Read(value, form.Key, value[..dot]) is not { } submission)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var time = services.GetService<TimeProvider>() ?? TimeProvider.System;
        var expired = tokens.Issue(form.Key, submission, value[..dot], time.GetUtcNow() - TimeSpan.FromMinutes(1));
        await context.Response.WriteAsJsonAsync(new { value = expired }, Json, context.RequestAborted);
    }

    // The name a stored signature's PNG holds as typed (ADR-0103); null for any other file.
    private static async Task<string?> TypedNameAsync(IFormBuilderFileStore store, string storedName, CancellationToken cancellationToken)
    {
        await using var content = await store.OpenReadAsync(storedName, cancellationToken);
        if (content is null)
        {
            return null;
        }

        using var bytes = new MemoryStream();
        await content.CopyToAsync(bytes, cancellationToken);
        return TypedSignature.NameOf(bytes.ToArray());
    }

    private sealed record ClockRequest(DateTimeOffset? Utc = null, double? AdvanceSeconds = null);

    private sealed record SentExpiredRequest(string? Form = null, string? Value = null);
}
