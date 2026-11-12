using System.Collections.Frozen;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Exchange;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.Outbound;
using Jiron.Umbraco.FormBuilder.Core.Settings;

namespace Jiron.Umbraco.FormBuilder.Core.Webhooks;

/// <summary>What a webhook's body is written as.</summary>
public enum WebhookFormat
{
    /// <summary>A JSON object.</summary>
    [FormBuilderChoice("#jfbActionChoices_webhookFormatJson")]
    Json,

    /// <summary><c>application/x-www-form-urlencoded</c>.</summary>
    [FormBuilderChoice("#jfbActionChoices_webhookFormatForm")]
    Form,

    /// <summary>An XML document.</summary>
    [FormBuilderChoice("#jfbActionChoices_webhookFormatXml")]
    Xml,
}

/// <summary>Where a webhook's address comes from.</summary>
public enum WebhookTarget
{
    /// <summary>The action's own address.</summary>
    [FormBuilderChoice("#jfbActionChoices_webhookTargetUrl")]
    Url,

    /// <summary>The site's Slack address, from the integrations settings.</summary>
    [FormBuilderChoice("#jfbActionChoices_webhookTargetSlack")]
    Slack,

    /// <summary>The site's Microsoft Teams address, from the integrations settings.</summary>
    [FormBuilderChoice("#jfbActionChoices_webhookTargetTeams")]
    Teams,
}

/// <summary>The settings of the webhook action (ROADMAP 8.4, ADR-0161).</summary>
public sealed class WebhookActionSettings
{
    /// <summary>Where it goes; its box draws the address within it (ADR-0191).</summary>
    [FormBuilderSetting("#jfbActionSettings_webhookTarget", Order = 1, Editor = "webhookAddress")]
    public WebhookTarget Target { get; init; }

    /// <summary>
    /// The address, or a <c>$JironFormBuilder:Secrets:&lt;Name&gt;</c> reference to one that is a secret itself; for Slack or Teams
    /// only such a reference, null for the integrations settings' address.
    /// </summary>
    [FormBuilderSetting("#jfbActionSettings_webhookUrl", Order = 2, Editor = "webhookUrl", Within = "target")]
    public string? Url { get; init; }

    /// <summary>What the body is written as.</summary>
    [FormBuilderSetting("#jfbActionSettings_webhookFormat", Order = 3, Within = "fields")]
    public WebhookFormat Format { get; init; }

    /// <summary>
    /// The field mapping, as <c>{"name": "{{ field.email }}"}</c>: each name with the text of its template. Null for every field
    /// a mail could show, by its alias. Its box, What it sends, draws the format, the body, the message and the switches within it
    /// (ADR-0191).
    /// </summary>
    [FormBuilderSetting("#jfbActionSettings_webhookFields", Order = 4, Editor = "webhookBody")]
    public JsonElement? Fields { get; init; }

    /// <summary>Whether it sends no field at all, only that something happened, for a receiver that then asks for what it may see.</summary>
    [FormBuilderSetting("#jfbActionSettings_webhookWithoutAnswers", Order = 5, Within = "fields")]
    public bool WithoutAnswers { get; init; }

    /// <summary>Whether the body also says what happened: the trigger, the delivery, the form and the entry.</summary>
    [FormBuilderSetting("#jfbActionSettings_webhookMetadata", Order = 6, Within = "fields")]
    public bool Metadata { get; init; } = true;

    /// <summary>The whole body as a template, in the format's own language, instead of the fields or a chat's message.</summary>
    [FormBuilderSetting("#jfbActionSettings_webhookTemplate", Order = 7, SupportsMergeTags = true, Within = "fields")]
    [StringLength(FieldSettingRules.HtmlLength)]
    public string? Template { get; init; }

    /// <summary>
    /// A Slack or Teams message, as text with merge tags in the chat's own formatting (ADR-0191); an own body wins over it, and
    /// without either a chat gets the answers under the form's name. Its address's own target ignores it.
    /// </summary>
    [FormBuilderSetting("#jfbActionSettings_webhookMessage", Order = 8, SupportsMergeTags = true, Within = "fields")]
    [StringLength(FieldSettingRules.HtmlLength)]
    public string? Message { get; init; }

    /// <summary>
    /// A <c>$JironFormBuilder:Secrets:&lt;Name&gt;</c> reference to the signing secret; null for the integrations settings' one. A
    /// request without any is not signed.
    /// </summary>
    [FormBuilderSetting("#jfbActionSettings_webhookSecret", Description = "#jfbActionSettings_webhookSecretHelp", Order = 9, Editor = "secretReference", Group = FieldSettingGroup.Advanced)]
    public string? Secret { get; init; }

    /// <summary>More headers, as <c>{"Authorization": "$JironFormBuilder:Secrets:CrmToken"}</c>; a value may be such a reference.</summary>
    [FormBuilderSetting("#jfbActionSettings_webhookHeaders", Description = "#jfbActionSettings_webhookHeadersHelp", Order = 10, Editor = "headerMap", Group = FieldSettingGroup.Advanced)]
    public JsonElement? Headers { get; init; }

    /// <summary>The sensitive fields it may send (ADR-0014); none by default.</summary>
    [FormBuilderSetting("#jfbActionSettings_webhookIncludeSensitive", Order = 11, Kind = FieldSettingKind.Key, Editor = "fieldPicker", Picks = FieldPick.Sensitive, Group = FieldSettingGroup.Advanced)]
    public IReadOnlyList<Guid> IncludeSensitive { get; init; } = [];
}

/// <summary>The <c>X-Jfb-Signature</c> header of a webhook (ROADMAP 8.4): an HMAC-SHA256 of the time and the body.</summary>
internal static class WebhookSignature
{
    /// <summary>The header's name.</summary>
    public const string Header = "X-Jfb-Signature";

    /// <summary>The header's value, as <c>t=&lt;unix seconds&gt;, v1=&lt;hex&gt;</c>.</summary>
    public static string Of(string secret, DateTimeOffset time, ReadOnlySpan<byte> body)
    {
        ArgumentNullException.ThrowIfNull(secret);
        var seconds = time.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var signed = new byte[seconds.Length + 1 + body.Length];
        Encoding.ASCII.GetBytes(seconds, signed);
        signed[seconds.Length] = (byte)'.';
        body.CopyTo(signed.AsSpan(seconds.Length + 1));
        return $"t={seconds}, v1={Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), signed))}";
    }
}

/// <summary>
/// The webhook action (ROADMAP 8.4, ADR-0161), and through its presets Slack and Microsoft Teams: one signed <c>POST</c> to a host the
/// configuration names, held to the site's outbound rules. An answer of <c>2xx</c> delivers it, a busy or failing host is tried again,
/// a refusal fails it for good, and a call that timed out is uncertain, as it may have arrived. A redirect is never followed, as it
/// would carry what is signed to wherever it says.
/// </summary>
internal sealed class WebhookActionType(OutboundHttp outbound, IFormBuilderMergeTagService tags, IFormBuilderSettingsService settings, FormBuilderSecrets secrets, TimeProvider time)
    : FormBuilderActionTypeBase<WebhookActionSettings>(Declared)
{
    /// <summary>The type's alias.</summary>
    public const string Alias = "webhook";

    /// <summary>How long a call may take; the attempt's own limit comes after it.</summary>
    public static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The code of a call whose secret the host configuration lacks.</summary>
    public const string SecretMissing = "secret_missing";

    /// <summary>The code of a call that has nowhere to go.</summary>
    public const string NoAddress = "no_address";

    /// <summary>The code of a call that timed out.</summary>
    public const string TimedOut = "timed_out";

    /// <summary>The code of a call that did not connect.</summary>
    public const string NoConnection = "no_connection";

    /// <summary>The code of a call the site's outbound rules refused.</summary>
    public const string NotAllowed = "not_allowed";

    /// <summary>The code of a call that would send a credential its settings hold themselves, as an unpublished draft can.</summary>
    public const string SecretLiteral = "secret_literal";

    private const string LiteralSigningSecret = "A signing secret is a reference such as $JironFormBuilder:Secrets:Crm, never the secret itself.";
    private const string LiteralChatAddress = "A Slack or Teams address holds its key: name it as a reference such as $JironFormBuilder:Secrets:Slack.";
    private const string LiteralHeader = "A header that carries a credential is a reference such as $JironFormBuilder:Secrets:Crm, never the credential itself.";

    /// <summary>The code of a call whose header, from a secret, cannot be sent.</summary>
    public const string HeaderInvalid = "header_invalid";

    /// <summary>The code of a call whose body would be cut at the merge tags' limit.</summary>
    public const string TooLong = MergeTagProblems.TooLong;

    // A message each, which the server writes as the chat's JSON (ADR-0191); presets of before held own bodies, which still go as they are.
    private static string ChatPreset(string target, string message) =>
        JsonSerializer.Serialize(new Dictionary<string, string>(StringComparer.Ordinal) { ["target"] = target, ["message"] = message });

    private static readonly ActionTypeDeclaration Declared = new()
    {
        Alias = Alias,
        Name = "#jfbActionTypes_webhook",
        Icon = "icon-link",
        Test = ActionTestKind.AfterConfirmation,
        // A plain webhook first: the designer offers each preset as a start, and a type only through its presets (ADR-0188).
        Presets =
        [
            new ActionPreset("#jfbActionTypes_webhook", "{}"),
            new ActionPreset("#jfbActionPresets_webhookSlack", ChatPreset("slack", WebhookChat.SlackMessage)),
            new ActionPreset("#jfbActionPresets_webhookTeams", ChatPreset("teams", WebhookChat.TeamsMessage)),
        ],
    };

    /// <inheritdoc />
    protected override void CheckSettings(WebhookActionSettings settings, ActionSettingsCheck check)
    {
        CheckAddress(settings, check);
        if (settings.Secret is { } secret && !FormBuilderSecrets.IsReference(secret))
        {
            check.Invalid("secret", LiteralSigningSecret);
        }

        var context = settings.Format switch
        {
            WebhookFormat.Form => MergeTagContext.Form,
            WebhookFormat.Xml => MergeTagContext.Xml,
            _ => MergeTagContext.Json,
        };
        CheckTemplate("template", settings.Template, context, check);
        CheckTemplate("message", settings.Message, MergeTagContext.Text, check);
        if (settings.WithoutAnswers && (settings.Fields is { ValueKind: not JsonValueKind.Null } || settings.Template is not null))
        {
            check.Invalid("withoutAnswers", "A webhook without answers has no mapping and no body of its own.");
        }

        if (WebhookMaps.Read(settings.Fields, WebhookMaps.IsFieldName) is not { } fields)
        {
            check.Invalid("fields", "The mapping is an object of names and templates; a name has letters, digits, '_', '-' and '.'.");
        }
        else
        {
            foreach (var template in fields.Values)
            {
                CheckTemplate("fields", template, MergeTagContext.Text, check);
            }
        }

        if (WebhookMaps.ReadHeaders(settings.Headers) is not { } headers)
        {
            check.Invalid("headers", "The headers are an object of names, each once, and plain ASCII texts; a name of Jiron Form Builder's own or of the request itself is refused.");
        }
        else if (headers.Any(header => WebhookMaps.HoldsASecret(header.Key) && !FormBuilderSecrets.IsReference(header.Value)))
        {
            check.Invalid("headers", LiteralHeader);
        }

        var known = DefinitionKeys.Fields(check.Definition).Select(field => field.Key).ToHashSet();
        if (settings.IncludeSensitive.Any(field => !known.Contains(field)))
        {
            check.Invalid("includeSensitive", "Each must be a field of this form.");
        }
    }

    /// <inheritdoc />
    protected override IReadOnlyList<string> MissingConfiguration(WebhookActionSettings settings, FormAction action)
    {
        var references = new[] { settings.Url, settings.Secret }.Concat(WebhookMaps.ReadHeaders(settings.Headers)?.Values ?? []);
        return [.. references.Where(value => FormBuilderSecrets.IsReference(value) && secrets.Resolve(value) is null).Select(value => FormBuilderSecrets.KeyOf(value!))];
    }

    /// <inheritdoc />
    protected override async Task<ActionResult> RunAsync(WebhookActionSettings settings, ActionRunContext context, CancellationToken cancellationToken)
    {
        var (status, failure, _) = await CallAsync(settings, context.Scope, context.Trigger.ToString(), context.DeliveryKey, testLine: null, cancellationToken);
        return failure ?? ResultOf(status);
    }

    /// <inheritdoc />
    protected override async Task<ActionTestResult> TestAsync(WebhookActionSettings settings, ActionTestContext context, CancellationToken cancellationToken)
    {
        // A chat's message says on its first line that it is a test, in the tester's language, as a test mail does (ADR-0191).
        var line = WebhookChat.TestLine(context.User.Culture);
        var (status, failure, missing) = await CallAsync(settings, context.Scope, nameof(ActionTrigger.Submitted), "test", line, cancellationToken);
        var result = failure ?? ResultOf(status);
        var answered = string.Create(CultureInfo.InvariantCulture, $"{status}");
        if (result.Kind == ActionResultKind.Succeeded)
        {
            return new ActionTestResult(ActionTestOutcome.Sent, $"The host answered {answered}.") { Code = ActionTestCodes.Sent, Args = [answered] };
        }

        // The run's code, which the backoffice says in its words, with the configuration key a secret the host lacks has, or the
        // target that has no address; the sentence names it for any other reader.
        var code = result.Code ?? ActionTestCodes.Failed;
        IReadOnlyList<string> args = missing is not null ? [missing] : code == NoAddress ? [JsonNamingPolicy.CamelCase.ConvertName(settings.Target.ToString())] : [];
        return new ActionTestResult(ActionTestOutcome.Failed, $"The webhook was not delivered ({code}).") { Code = code, Args = args };
    }

    private static ActionResult ResultOf(int status) => status switch
    {
        >= 200 and < 300 => ActionResult.Succeeded,
        408 or 425 or 429 or >= 500 => ActionResult.TryAgain(HttpCode(status)),
        _ => ActionResult.Failed(HttpCode(status)),
    };

    /// <summary>
    /// The settings of the action that hold a secret as itself where only a reference may stand, each with why: what a save
    /// of a new or changed action, publishing and a run refuse (16.1's review).
    /// </summary>
    /// <param name="action">The action's settings.</param>
    internal static IEnumerable<(string Setting, string Message)> LiteralSecrets(WebhookActionSettings action)
    {
        if (action.Secret is not null && !FormBuilderSecrets.IsReference(action.Secret))
        {
            yield return ("secret", LiteralSigningSecret);
        }

        if (action.Target != WebhookTarget.Url && action.Url is not null && !FormBuilderSecrets.IsReference(action.Url))
        {
            yield return ("url", LiteralChatAddress);
        }

        if (WebhookMaps.ReadHeaders(action.Headers)?.Any(header => WebhookMaps.HoldsASecret(header.Key) && !FormBuilderSecrets.IsReference(header.Value)) ?? false)
        {
            yield return ("headers", LiteralHeader);
        }
    }

    // What a run refuses, as a test run of a draft has not been through publishing's checks.
    private static bool HoldsALiteralSecret(WebhookActionSettings action) => LiteralSecrets(action).Any();

    private static string HttpCode(int status) => string.Create(CultureInfo.InvariantCulture, $"http_{status}");

    private static string ContentTypeOf(WebhookFormat format) => format switch
    {
        WebhookFormat.Form => "application/x-www-form-urlencoded",
        WebhookFormat.Xml => "application/xml; charset=utf-8",
        _ => "application/json; charset=utf-8",
    };

    private void CheckAddress(WebhookActionSettings settings, ActionSettingsCheck check)
    {
        if (settings.Target != WebhookTarget.Url)
        {
            // Such an address holds its own key, so like the integrations settings' it is a reference.
            if (settings.Url is not null && !FormBuilderSecrets.IsReference(settings.Url))
            {
                check.Invalid("url", LiteralChatAddress);
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(settings.Url))
        {
            check.Invalid("url", "A webhook needs its address.");
        }
        else if (!FormBuilderSecrets.IsReference(settings.Url)
            && (!Uri.TryCreate(settings.Url, UriKind.Absolute, out var address) || outbound.Refusal(address) is not null))
        {
            check.Invalid("url", "The address must be one the host configuration allows: https, on a host in JironFormBuilder:Webhooks:AllowedHosts.");
        }
    }

    private void CheckTemplate(string setting, string? template, MergeTagContext context, ActionSettingsCheck check)
    {
        if (template is null)
        {
            return;
        }

        // Unlike a mail, whose reader sees a gap, a tag that renders nothing can break the body for a receiver nobody watches.
        foreach (var problem in tags.Check(template, MergeTagPlace.Action, context, check.Definition))
        {
            check.Invalid(setting, $"A merge tag at {problem.At} cannot be used here: {problem.Code}.");
        }
    }

    // What a call came to: the host's status, or why it failed, with the configuration key of a secret the host lacks, which is
    // no secret itself.
    private static (int Status, ActionResult? Failure, string? Missing) Fails(ActionResult failure) => (0, failure, null);

    private static (int Status, ActionResult? Failure, string? Missing) Lacks(string reference)
    {
        var key = FormBuilderSecrets.KeyOf(reference);
        return (0, ActionResult.Failed(SecretMissing, $"The host configuration has no {key}."), key);
    }

    // A test's call carries its line for a chat's message, and is marked as one; a run's has none.
    private async Task<(int Status, ActionResult? Failure, string? Missing)> CallAsync(WebhookActionSettings action, MergeTagScope scope, string trigger, string delivery, string? testLine, CancellationToken cancellationToken)
    {
        if (HoldsALiteralSecret(action))
        {
            return Fails(ActionResult.Failed(SecretLiteral));
        }

        var integrations = (await settings.GetAsync<IntegrationSettings>(cancellationToken: cancellationToken)).Value;
        var written = action.Target switch
        {
            WebhookTarget.Slack => action.Url ?? integrations.SlackWebhook,
            WebhookTarget.Teams => action.Url ?? integrations.TeamsWebhook,
            _ => action.Url,
        };
        if (string.IsNullOrWhiteSpace(written))
        {
            return Fails(ActionResult.Failed(NoAddress, "The action has no address, and the integrations settings none for it."));
        }

        var signing = action.Secret ?? integrations.SigningSecret;
        if (!secrets.TryResolve(written, out var url))
        {
            return Lacks(written);
        }

        if (!secrets.TryResolve(signing, out var secret))
        {
            return Lacks(signing!);
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var address))
        {
            return Fails(ActionResult.Failed(NotAllowed));
        }

        var headers = new List<KeyValuePair<string, string>> { new("X-Jfb-Delivery", delivery) };
        foreach (var (name, value) in WebhookMaps.ReadHeaders(action.Headers) ?? new Dictionary<string, string>())
        {
            if (!secrets.TryResolve(value, out var header))
            {
                return Lacks(value);
            }

            if (!WebhookMaps.IsHeaderValue(header ?? string.Empty))
            {
                return Fails(ActionResult.Failed(HeaderInvalid));
            }

            headers.Add(new(name, header ?? string.Empty));
        }

        var guarded = scope with
        {
            IncludedSensitive = action.IncludeSensitive.ToHashSet(),
            GuardsVisitorText = action.Target switch
            {
                WebhookTarget.Slack => WebhookChatText.Slack,
                WebhookTarget.Teams => WebhookChatText.Teams,
                _ => null,
            },
        };
        if (await new WebhookBody(tags).WriteAsync(action, guarded, trigger, delivery, testLine, cancellationToken) is not { } body)
        {
            return Fails(ActionResult.Failed(TooLong));
        }

        if (secret is not null)
        {
            headers.Add(new(WebhookSignature.Header, WebhookSignature.Of(secret, time.GetUtcNow(), body)));
        }

        if (testLine is not null)
        {
            headers.Add(new("X-Jfb-Test", "true"));
        }

        var (status, failure) = await PostAsync(address, body, action.Format, headers, cancellationToken);
        return (status, failure, null);
    }

    // What came of the call. Whether a broken connection delivered is unknowable, but the delivery key lets a receiver see a repeat,
    // and a host that is down is the usual reason; only a call that went out and got no answer in time asks a person.
    private async Task<(int Status, ActionResult? Failure)> PostAsync(Uri address, byte[] body, WebhookFormat format, List<KeyValuePair<string, string>> headers, CancellationToken cancellationToken)
    {
        try
        {
            return (await outbound.PostAsync(address, body, ContentTypeOf(format), headers, CallTimeout, cancellationToken), null);
        }
        catch (FormBuilderOutboundException exception)
        {
            return (0, exception.Reason switch
            {
                OutboundFailure.TimedOut => ActionResult.Uncertain(TimedOut),
                OutboundFailure.NoConnection => ActionResult.TryAgain(NoConnection),
                _ => ActionResult.Failed(NotAllowed, exception.Reason.ToString()),
            });
        }
    }
}

/// <summary>
/// Keeps what a visitor typed as text in a chat message (ADR-0161): unlike a mail's, a chat's message is markup even when its JSON is
/// sound, so <c>&lt;!channel&gt;</c> would call everyone and <c>[Open](https://...)</c> would be a link that says what it likes.
/// </summary>
internal static class WebhookChatText
{
    /// <summary>The text with the three characters Slack's messages read as markup written as Slack says to write them.</summary>
    public static string Slack(string text) => text
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal);

    /// <summary>
    /// The text with a zero-width space after each <c>]</c>, so no link's text ends in it to meet an address, in this answer or the
    /// next one; a reader sees the same words.
    /// </summary>
    public static string Teams(string text) => text.Replace("]", "]" + (char)0x200b, StringComparison.Ordinal);
}

/// <summary>The mapping and header objects of a webhook's settings.</summary>
internal static class WebhookMaps
{
    private static readonly FrozenSet<string> OwnHeaders = new[]
    {
        "allow", "connection", "expect", "expires", "host", "keep-alive", "last-modified", "proxy-connection", "te", "trailer", "transfer-encoding", "upgrade", "user-agent", "via",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    // A header whose name holds one of these carries a credential, which only a reference may give (16.1's review widened
    // them past auth, cookie, key, password, secret and token).
    private static readonly FrozenSet<string> SecretWords = new[] { "auth", "bearer", "cookie", "credential", "key", "passphrase", "passwd", "password", "secret", "session", "signature", "token" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Whether a text is a name of the mapping.</summary>
    public static bool IsFieldName(string name) =>
        name.Length is > 0 and <= 100 && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.');

    /// <summary>Whether a text is a header's name a site may set.</summary>
    public static bool IsHeaderName(string name) =>
        name.Length is > 0 and <= 100
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
        && !name.StartsWith("x-jfb-", StringComparison.OrdinalIgnoreCase)
        && !name.StartsWith("content-", StringComparison.OrdinalIgnoreCase)
        && !OwnHeaders.Contains(name);

    /// <summary>Whether a text can be a header's value: what HTTP carries as it is.</summary>
    public static bool IsHeaderValue(string value) => value.All(c => c is >= ' ' and <= '~');

    /// <summary>The headers of a webhook's settings; null when they are not an object of names, each once, and values HTTP can carry.</summary>
    public static IReadOnlyDictionary<string, string>? ReadHeaders(JsonElement? element)
    {
        var headers = Read(element, IsHeaderName);
        return headers is null || !headers.Values.All(IsHeaderValue) || headers.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != headers.Count ? null : headers;
    }

    /// <summary>Whether a header's name says it carries a credential, which only a reference may give.</summary>
    public static bool HoldsASecret(string name) =>
        SecretWords.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase));

    /// <summary>The object's names and texts; empty for none, null when it is not such an object.</summary>
    public static IReadOnlyDictionary<string, string>? Read(JsonElement? element, Func<string, bool> isName)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (element is not { ValueKind: not JsonValueKind.Null } given)
        {
            return map;
        }

        if (given.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in given.EnumerateObject())
        {
            if (!isName(property.Name) || property.Value.ValueKind != JsonValueKind.String || property.Value.GetString()!.Any(char.IsControl))
            {
                return null;
            }

            map[property.Name] = property.Value.GetString()!;
        }

        return map;
    }
}
