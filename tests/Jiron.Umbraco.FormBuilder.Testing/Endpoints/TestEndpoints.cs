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
/// host has none.</description></item>
/// <item><description><c>GET /jfb-testing/access</c>: each backoffice user's effective Jiron Form Builder permissions,
/// globally, at the root and on each root folder outside the trash in the library's order (ADR-0049), so the browser
/// tests can see that the personas have their grants.</description></item>
/// <item><description><c>GET /jfb-testing/entries?form=&lt;alias&gt;</c>: the form's entries, oldest first, each with its
/// stored values by the field's key, row and item (ADR-0100) and its files by the field's key, name, size and the name
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
/// 2,000.</description></item>
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
        var settingsService = context.RequestServices.GetRequiredService<IFormBuilderSettingsService>();
        var general = await settingsService.GetAsync<GeneralSettings>(null, context.RequestAborted);
        if (general.Value.BaseUrl is null)
        {
            _ = await settingsService.SaveAsync(general.Value with { BaseUrl = $"{context.Request.Scheme}://{context.Request.Host}" }, general.Version, null, context.RequestAborted);
        }

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
        var name = new FormField { Key = Guid.NewGuid(), Type = "text", Alias = "name", Label = new LocalizedText("Name") };
        var position = new FormField
        {
            Key = Guid.NewGuid(),
            Type = "dropdown",
            Alias = "position",
            Label = new LocalizedText("Position"),
            Options = new FieldOptions { Items = [.. positions.Select((label, index) => new FieldOption { Value = $"p{index + 1}", Label = new LocalizedText(label) })] },
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
                Actions = [hook],
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

        await context.Response.WriteAsJsonAsync(new { key = created.Key, alias = published.Alias, positions }, Json, context.RequestAborted);
    }

    private sealed record EntriesFormRequest(int? Count, string? Url, string? Name);

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

        var states = new Dictionary<long, string?>();
        foreach (var id in ids)
        {
            states[id] = (await entries.GetByIdAsync(id, context.RequestAborted))?.State.ToString();
        }

        var answer = ids.Order().Select(id => new
        {
            id,
            state = states[id],
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
