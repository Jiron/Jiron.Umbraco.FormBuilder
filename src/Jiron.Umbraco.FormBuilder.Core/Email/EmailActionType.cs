using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Exchange;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Mail;
using Umbraco.Cms.Core.Services;

namespace Jiron.Umbraco.FormBuilder.Core.Email;

/// <summary>The settings of the email action (ROADMAP 8.3, ADR-0160).</summary>
public sealed class EmailActionSettings
{
    /// <summary>Fixed addresses it goes to, usually the team's; its editor holds every kind of recipient (ADR-0189).</summary>
    [FormBuilderSetting("#jfbActionSettings_emailTo", Order = 1, Editor = "recipients")]
    public IReadOnlyList<string> To { get; init; } = [];

    /// <summary>
    /// An email field whose answer it goes to. That address is one a visitor typed: it gets a mail of its own, which the limits
    /// of section 4.10 count, and links in what visitors typed are broken there.
    /// </summary>
    [FormBuilderSetting("#jfbActionSettings_emailToField", Order = 2, Kind = FieldSettingKind.Key, Editor = "fieldPicker", Picks = FieldPick.Email, Within = "to")]
    public Guid? ToField { get; init; }

    /// <summary>Whether it goes to the user who created the form.</summary>
    [FormBuilderSetting("#jfbActionSettings_emailToCreator", Order = 3, Within = "to")]
    public bool ToCreator { get; init; }

    /// <summary>
    /// Addresses chosen by a choice field's answer, as <c>{"field": key, "routes": [{"value": "sales", "to": ["a@example.com"]}]}</c>.
    /// They stay on the server, as actions do (section 4.3.1).
    /// </summary>
    [FormBuilderSetting("#jfbActionSettings_emailRecipientMap", Order = 4, Editor = "recipientMap", Within = "to")]
    public JsonElement? RecipientMap { get; init; }

    /// <summary>Fixed addresses that get a copy.</summary>
    [FormBuilderSetting("#jfbActionSettings_emailCc", Order = 9, Editor = "addresses")]
    public IReadOnlyList<string> Cc { get; init; } = [];

    /// <summary>Fixed addresses that get a blind copy.</summary>
    [FormBuilderSetting("#jfbActionSettings_emailBcc", Order = 10, Editor = "addresses", Group = FieldSettingGroup.Advanced)]
    public IReadOnlyList<string> Bcc { get; init; } = [];

    /// <summary>An email field whose answer replies go to, when that answer is an address.</summary>
    [FormBuilderSetting("#jfbActionSettings_emailReplyTo", Order = 8, Kind = FieldSettingKind.Key, Editor = "fieldPicker", Picks = FieldPick.Email)]
    public Guid? ReplyToField { get; init; }

    /// <summary>The address it comes from instead of the site's; a host usually locks it. Never a visitor's.</summary>
    [FormBuilderSetting("#jfbActionSettings_emailFrom", Order = 11, Group = FieldSettingGroup.Advanced)]
    public string? From { get; init; }

    /// <summary>The subject per culture, with merge tags.</summary>
    [FormBuilderSetting("#jfbActionSettings_emailSubject", Order = 5, SupportsMergeTags = true)]
    public LocalizedText? Subject { get; init; }

    /// <summary>The body per culture, with merge tags; <c>{{ entry.table }}</c> gives every answer as a table.</summary>
    [FormBuilderSetting("#jfbActionSettings_emailBody", Order = 6, SupportsMergeTags = true)]
    public LocalizedHtml? Body { get; init; }

    /// <summary>Whether <c>{{ entry.table }}</c> leaves out the fields without an answer.</summary>
    [FormBuilderSetting("#jfbActionSettings_emailWithoutUnanswered", Order = 7)]
    public bool WithoutUnanswered { get; init; }

    /// <summary>Whether it is one plain-text body instead of HTML (ADR-0007).</summary>
    [FormBuilderSetting("#jfbActionSettings_emailPlainText", Order = 12, Group = FieldSettingGroup.Appearance)]
    public bool PlainText { get; init; }

    /// <summary>The key of the layout, or null for the site's.</summary>
    [FormBuilderSetting("#jfbActionSettings_emailLayout", Order = 13, Kind = FieldSettingKind.Key, Editor = "emailLayoutPicker", Group = FieldSettingGroup.Appearance)]
    public Guid? Layout { get; init; }

    /// <summary>A Razor view in <c>Views/JironFormBuilder/Emails</c>, by its name without <c>.cshtml</c>, that writes the body instead.</summary>
    [FormBuilderSetting("#jfbActionSettings_emailView", Order = 14, Group = FieldSettingGroup.Advanced)]
    public string? View { get; init; }

    /// <summary>The sensitive fields it may show (ADR-0014); none by default.</summary>
    [FormBuilderSetting("#jfbActionSettings_emailIncludeSensitive", Order = 15, Kind = FieldSettingKind.Key, Editor = "fieldPicker", Picks = FieldPick.Sensitive, Group = FieldSettingGroup.Advanced)]
    public IReadOnlyList<Guid> IncludeSensitive { get; init; } = [];

    /// <summary>The upload fields whose files go along; none by default.</summary>
    [FormBuilderSetting("#jfbActionSettings_emailAttachments", Order = 16, Kind = FieldSettingKind.Key, Editor = "fieldPicker", Picks = FieldPick.File, Group = FieldSettingGroup.Advanced)]
    public IReadOnlyList<Guid> Attachments { get; init; } = [];
}

/// <summary>What a Razor view of a mail gets (ROADMAP 8.3): the entry's answers as merge tags would list them.</summary>
public sealed record EmailViewModel
{
    /// <summary>The subject, as it was rendered.</summary>
    public required string Subject { get; init; }

    /// <summary>The culture the mail is in.</summary>
    public required string Culture { get; init; }

    /// <summary>The form's name.</summary>
    public required string FormName { get; init; }

    /// <summary>The entry's number within its form.</summary>
    public required int EntryNumber { get; init; }

    /// <summary>The answers as <c>{{ entry.answers }}</c> writes them, one line each, for a view to encode as it writes them.</summary>
    public required string Answers { get; init; }
}

/// <summary>Writes the body of a mail with a Razor view of the site's; the website project brings it.</summary>
public interface IFormBuilderEmailView
{
    /// <summary>The body, or null when the site has no such view.</summary>
    /// <param name="view">The view's name without <c>.cshtml</c>: letters, digits, '-' and '_'.</param>
    /// <param name="model">What the view gets.</param>
    /// <param name="cancellationToken">Cancelled when the attempt is.</param>
    Task<string?> RenderAsync(string view, EmailViewModel model, CancellationToken cancellationToken);
}

/// <summary>The names a mail's view may have, here and where it is found: never a path.</summary>
internal static class EmailViewNames
{
    private static readonly System.Buffers.SearchValues<char> Allowed = System.Buffers.SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_");

    /// <summary>Whether a text is a view's name: 1 to 100 letters, digits, '-' and '_'.</summary>
    public static bool IsName(string? view) => view is { Length: > 0 and <= 100 } && !view.AsSpan().ContainsAnyExcept(Allowed);
}

/// <summary>A run's view the site lacks, with no body of its own to fall back on.</summary>
internal sealed class ViewMissingException(string message) : Exception(message);

/// <summary>
/// The mail an email action sends for an entry, as the team gets it, with everyone a run sends it to (ADR-0189); nothing
/// is queued.
/// </summary>
/// <param name="Mail">The team's mail as the site sends it, its recipients the team's addresses; none when it has only a visitor's.</param>
/// <param name="Visitor">The address a mail of its own goes to: the answer of the recipient field, when it is one address.</param>
internal sealed record EmailPreview(ComposedEmail Mail, string? Visitor);

/// <summary>A route of the recipient map: an option's value, and who hears of it.</summary>
internal sealed record EmailRoute
{
    /// <summary>The option's value.</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>The addresses.</summary>
    public IReadOnlyList<string> To { get; init; } = [];
}

/// <summary>The recipient map as it is stored.</summary>
internal sealed record EmailRecipientMap
{
    private static readonly JsonSerializerOptions Json = Options();

    /// <summary>The choice field.</summary>
    public Guid Field { get; init; }

    /// <summary>The routes.</summary>
    public IReadOnlyList<EmailRoute> Routes { get; init; } = [];

    private static JsonSerializerOptions Options()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    /// <summary>The map of an action's settings; null for none or one that is not a map.</summary>
    public static EmailRecipientMap? Of(JsonElement? element, out bool isValid)
    {
        isValid = true;
        if (element is not { ValueKind: not JsonValueKind.Null } map)
        {
            return null;
        }

        try
        {
            return map.Deserialize<EmailRecipientMap>(Json);
        }
        catch (JsonException)
        {
            isValid = false;
            return null;
        }
    }
}

/// <summary>
/// The email action (ROADMAP 8.3, ADR-0160): it renders its mail for the entry and puts it in the queue in one unit of work, so
/// the mail of one attempt queues whole or not at all and the action's run ends once it is queued; each mail then has its own
/// attempts in the log. Team addresses share one mail. An address a visitor typed gets a mail of its own, within the limits, in
/// which links in what visitors typed are broken.
/// </summary>
internal sealed partial class EmailActionType(
    EmailOutbox outbox,
    IFormBuilderMergeTagService tags,
    IEmailRepository emails,
    IEntryRepository entries,
    IFormRepository forms,
    IUserService users,
    FormBuilderDatabase database,
    IEnumerable<IFormBuilderEmailView> views,
    IEmailSender sender,
    ILogger<EmailActionType> logger)
    : FormBuilderActionTypeBase<EmailActionSettings>(Declared)
{
    /// <summary>The type's alias.</summary>
    public const string Alias = "email";

    /// <summary>The code of a run whose view the site lacks, with no body of its own to fall back on.</summary>
    public const string ViewMissing = "view_missing";

    private const string EmailFieldType = "email";

    private const string NotifyTexts = """
        "subject":{"":"New entry in {{ form.name }}","de":"Neuer Eintrag in {{ form.name }}"},"body":{"":"<p>A new entry came in.</p>{{ entry.table }}","de":"<p>Ein neuer Eintrag ist eingegangen.</p>{{ entry.table }}"}
        """;

    private const string ConfirmTexts = """
        "subject":{"":"We have your answers","de":"Wir haben Ihre Angaben erhalten"},"body":{"":"<p>Thank you. We have your answers.</p>{{ entry.table }}","de":"<p>Vielen Dank. Wir haben Ihre Angaben erhalten.</p>{{ entry.table }}"}
        """;

    private static readonly ActionTypeDeclaration Declared = new()
    {
        Alias = Alias,
        Name = "#jfbActionTypes_email",
        Icon = "icon-message",
        Test = ActionTestKind.ToCurrentUser,
        Presets =
        [
            new ActionPreset("#jfbActionPresets_emailNotifyTeam", "{" + NotifyTexts + "}"),
            new ActionPreset("#jfbActionPresets_emailConfirmSender", "{" + ConfirmTexts + "}"),
        ],
    };

    /// <summary>The settings of the action to the user who created the form: the new-form default (ADR-0044).</summary>
    public static SettingsObject NotifyCreatorSettings() => SettingsObject.Parse("""{"toCreator":true,""" + NotifyTexts + "}");

    /// <inheritdoc />
    protected override void CheckSettings(EmailActionSettings settings, ActionSettingsCheck check)
    {
        var fields = DefinitionKeys.Fields(check.Definition).ToDictionary(field => field.Key);
        var map = EmailRecipientMap.Of(settings.RecipientMap, out var mapIsValid);
        CheckAddresses("to", settings.To, check);
        CheckAddresses("cc", settings.Cc, check);
        CheckAddresses("bcc", settings.Bcc, check);
        if (settings.From is { } from && !EmailAddresses.IsOne(from))
        {
            check.Invalid("from", "This is not one address, such as hello@example.com.");
        }

        var own = check.Definition.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields).ToDictionary(field => field.Key);
        CheckEmailField("toField", settings.ToField, own, check);
        CheckEmailField("replyToField", settings.ReplyToField, own, check);
        CheckFields("includeSensitive", settings.IncludeSensitive, fields, check);
        CheckFields("attachments", settings.Attachments, fields, check);
        if (settings.View is { } view && !EmailViewNames.IsName(view))
        {
            check.Invalid("view", "A view's name has letters, digits, '-' and '_'.");
        }

        if (!mapIsValid)
        {
            check.Invalid("recipientMap", "This is not a recipient map.");
        }
        else if (map is not null)
        {
            CheckMap(map, own, check);
        }

        CheckTemplate("subject", settings.Subject, MergeTagContext.Header, check);
        CheckTemplate("body", settings.Body, settings.PlainText ? MergeTagContext.Text : MergeTagContext.Html, check);

        if (settings.To.Count == 0 && settings.ToField is null && !settings.ToCreator && map is null)
        {
            check.Invalid("to", "A mail needs someone to go to.");
        }

        if (settings.Subject is null)
        {
            check.Invalid("subject", "A mail needs a subject.");
        }

        if (settings.Body is null && settings.View is null)
        {
            check.Invalid("body", "A mail needs a body or a view.");
        }
    }

    /// <inheritdoc />
    protected override async Task<ActionResult> RunAsync(EmailActionSettings settings, ActionRunContext context, CancellationToken cancellationToken)
    {
        var prepared = new List<NewEmail>();
        try
        {
            // A run that is tried again finds what an attempt before it queued: a commit can succeed and still answer with an error.
            if (context.EntryId is { } entryId && context.RunId is { } runId && (await emails.ListForEntryAsync(entryId, cancellationToken)).Any(mail => mail.ActionRunId == runId))
            {
                return ActionResult.Succeeded;
            }

            var (team, visitors) = await RecipientsAsync(settings, context, quiet: false, cancellationToken);
            if (team.Count > 0 || visitors.Count > 0)
            {
                prepared.AddRange(await TeamMailAsync(settings, context, team, visitors.Count > 0, cancellationToken));
                foreach (var visitor in visitors)
                {
                    var mail = await MailAsync(settings, context, [visitor], toVisitor: true, quiet: false, cancellationToken);
                    prepared.AddRange(await outbox.PrepareAsync(mail, cancellationToken) is { } ready ? [ready] : []);
                }
            }
        }
        catch (ViewMissingException exception)
        {
            return ActionResult.Failed(ViewMissing, exception.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Nothing is queued yet, so the next attempt may go without a question.
            return ActionResult.TryAgain(ActionAttempts.HostStopped);
        }

        if (prepared.Count == 0)
        {
            // Nothing to send to, as a visitor left the field empty or a map has no route: not a failure of the mail.
            return ActionResult.Succeeded;
        }

        await database.WriteAsync(
            async _ =>
            {
                foreach (var mail in prepared)
                {
                    await outbox.QueueAsync(mail, cancellationToken);
                }

                return true;
            },
            cancellationToken);
        return ActionResult.Succeeded;
    }

    /// <inheritdoc />
    protected override async Task<ActionTestResult> TestAsync(EmailActionSettings settings, ActionTestContext context, CancellationToken cancellationToken)
    {
        if (!EmailAddresses.IsOne(context.User.Email))
        {
            return new ActionTestResult(ActionTestOutcome.Failed, "Your account has no address a test mail can go to.") { Code = ActionTestCodes.NoAddress };
        }

        // A mail the site cannot send would wait in the queue, and the test would say it went (ADR-0190).
        if (!sender.CanSendRequiredEmail())
        {
            return new ActionTestResult(ActionTestOutcome.Failed, "This site cannot send mail yet: it has no mail server or pickup folder.") { Code = ActionTestCodes.MailOff };
        }

        var run = new ActionRunContext { Action = context.Action, Trigger = ActionTrigger.Submitted, Scope = context.Scope, Attempt = 1, DeliveryKey = string.Empty };
        OutgoingEmail mail;
        try
        {
            // The real mail, to the tester alone, its first line naming whom the real one goes to, routed as a run routes it.
            var (team, visitors) = await RecipientsAsync(settings, run, quiet: true, cancellationToken);
            var real = TeamMails(settings, team, visitors.Count > 0)
                .SelectMany(each => each.To.Concat(each.Cc).Concat(each.Bcc))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var line = TestLine(context.User.Culture, real, visitors);
            mail = await MailAsync(settings, run, [context.User.Email!], toVisitor: false, quiet: true, cancellationToken);
            mail = mail with { Body = settings.PlainText ? line + "\n\n" + mail.Body : "<p>" + WebUtility.HtmlEncode(line) + "</p>" + mail.Body };
        }
        catch (ViewMissingException exception)
        {
            return new ActionTestResult(ActionTestOutcome.Failed, exception.Message) { Code = ViewMissing };
        }

        var stored = await outbox.QueueAsync(mail with { Kind = EmailKind.Test, EntryId = null, ActionRunId = null }, cancellationToken);
        return stored is null
            ? new ActionTestResult(ActionTestOutcome.Failed, "The test mail could not be queued.") { Code = ActionTestCodes.Failed }
            : new ActionTestResult(ActionTestOutcome.Sent, $"Sent to {context.User.Email}", [stored.Subject, stored.TextBody]) { Code = ActionTestCodes.SentToYou };
    }

    // A test mail's first line, in the tester's backoffice language (English or German, as the setup test mail): that it is
    // one, and whom the real mail goes to (ADR-0190).
    private static string TestLine(string? culture, List<string> team, List<string> visitors)
    {
        var german = string.Equals(culture?.Split('-')[0], "de", StringComparison.OrdinalIgnoreCase);
        var to = string.Join(", ", team);
        var own = string.Join(", ", visitors);
        return (team.Count > 0, visitors.Count > 0, german) switch
        {
            (true, true, false) => $"This is a test. The real email goes to {to}, and to {own} in a mail of its own.",
            (true, false, false) => $"This is a test. The real email goes to {to}.",
            (false, true, false) => $"This is a test. The real email goes to {own}, in a mail of its own.",
            (false, false, false) => "This is a test. The real email goes to no one yet.",
            (true, true, true) => $"Dies ist ein Test. Die echte E-Mail geht an {to} und in einer eigenen E-Mail an {own}.",
            (true, false, true) => $"Dies ist ein Test. Die echte E-Mail geht an {to}.",
            (false, true, true) => $"Dies ist ein Test. Die echte E-Mail geht in einer eigenen E-Mail an {own}.",
            _ => "Dies ist ein Test. Die echte E-Mail geht noch an niemanden.",
        };
    }

    /// <summary>
    /// The mail a run of the action sends for the scope's entry, as the team gets it, queueing nothing (ADR-0189): the
    /// recipients as the run finds them, the copies, the reply-to, and the mail in the site's sender and layout, routed as
    /// a run routes it. Without a team, it is the mail to the visitor's address, which carries no copy and no reply-to,
    /// unless copies make a team's mail of their own; without either, the team's mail as it would be. It logs nothing a
    /// run would warn of: nothing is sent.
    /// </summary>
    /// <param name="action">The action as the site runs it, its locks applied.</param>
    /// <param name="scope">The entry, such as the sample entry.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <exception cref="ViewMissingException">The action names a view the site lacks and has no body of its own.</exception>
    internal async Task<EmailPreview> PreviewAsync(FormAction action, MergeTagScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(scope);
        var settings = ReadSettings(action.Settings);
        var run = new ActionRunContext { Action = action, Trigger = action.Trigger, Scope = scope, Attempt = 1, DeliveryKey = string.Empty };
        var (team, visitors) = await RecipientsAsync(settings, run, quiet: true, cancellationToken);
        var visitor = visitors.FirstOrDefault();
        if (TeamMails(settings, team, visitor is not null).FirstOrDefault() is { } first)
        {
            var mail = await MailAsync(settings, run, first.To, toVisitor: false, quiet: true, cancellationToken) with
            {
                Cc = first.Cc,
                Bcc = first.Bcc,
                ReplyTo = ReplyTo(settings, scope),
            };
            return new EmailPreview(await outbox.ComposeAsync(mail, cancellationToken), visitor);
        }

        if (visitor is not null)
        {
            // The visitor's own mail, by the address it goes to; its To is the team's, which has none.
            return new EmailPreview(await outbox.ComposeAsync(await MailAsync(settings, run, team, toVisitor: true, quiet: true, cancellationToken), cancellationToken), visitor);
        }

        var nobody = await MailAsync(settings, run, team, toVisitor: false, quiet: true, cancellationToken) with
        {
            Cc = settings.Cc,
            Bcc = settings.Bcc,
            ReplyTo = ReplyTo(settings, scope),
        };
        return new EmailPreview(await outbox.ComposeAsync(nobody, cancellationToken), null);
    }

    private static void CheckAddresses(string setting, IReadOnlyList<string> addresses, ActionSettingsCheck check)
    {
        if (addresses.Any(address => !EmailAddresses.IsOne(address)))
        {
            check.Invalid(setting, "Each must be one address, such as hello@example.com.");
        }
    }

    private static void CheckEmailField(string setting, Guid? key, Dictionary<Guid, FormField> fields, ActionSettingsCheck check)
    {
        if (key is { } field && (!fields.TryGetValue(field, out var found) || found.Type != EmailFieldType))
        {
            check.Invalid(setting, "This must be an email field of this form.");
        }
    }

    private void CheckTemplate(string setting, LocalizedString? text, MergeTagContext context, ActionSettingsCheck check)
    {
        foreach (var culture in text?.Texts ?? [])
        {
            foreach (var problem in tags.Check(culture.Text, MergeTagPlace.Mail, context, check.Definition))
            {
                // A tag that names nothing renders empty: the form check warns (ADR-0177).
                if (problem.IsWarning)
                {
                    check.Warn(setting, problem.Code, MergeTagFormCheck.Sentence(problem));
                }
                else
                {
                    check.Invalid(setting, $"A merge tag at {problem.At} cannot be used here: {problem.Code}.");
                }
            }
        }
    }

    private static void CheckFields(string setting, IReadOnlyList<Guid> keys, Dictionary<Guid, FormField> fields, ActionSettingsCheck check)
    {
        if (keys.Any(key => !fields.ContainsKey(key)))
        {
            check.Invalid(setting, "Each must be a field of this form.");
        }
    }

    private static void CheckMap(EmailRecipientMap map, Dictionary<Guid, FormField> fields, ActionSettingsCheck check)
    {
        if (!fields.TryGetValue(map.Field, out var field) || field.Options is not { } options)
        {
            check.Invalid("recipientMap", "The map's field must be a choice field of this form.");
            return;
        }

        if (map.Routes is not { Count: > 0 } || map.Routes.Any(route => route is not { Value.Length: > 0, To.Count: > 0 } || route.To.Any(address => !EmailAddresses.IsOne(address))))
        {
            check.Invalid("recipientMap", "Each route has an option's value and at least one address.");
        }
        else if (options.Source == OptionSource.Items && map.Routes.Any(route => options.Items.All(item => item.Value != route.Value)))
        {
            check.Invalid("recipientMap", "Each route's value must be one of the field's options.");
        }
    }

    private static string? AnswerOf(MergeTagScope scope, Guid field) => scope.Answers.Find(field)?.Values[0].Text;

    // The answer of the reply-to field, unless it is a sensitive field the action does not name.
    private static string? ReplyTo(EmailActionSettings settings, MergeTagScope scope) =>
        settings.ReplyToField is { } field && MayShow(settings, scope, field) ? AnswerOf(scope, field)?.Trim() : null;

    // Who the team's mails go to (ADR-0160): one mail to the team with its copies. Copies on a mail only a visitor's
    // address gets go as a mail of their own: the copies as its To, a blind copy still blind; blind copies alone each go
    // alone, so none sees another.
    private static List<TeamMail> TeamMails(EmailActionSettings settings, List<string> team, bool hasVisitor)
    {
        if (team.Count > 0)
        {
            return [new TeamMail(team, settings.Cc, settings.Bcc)];
        }

        if (!hasVisitor)
        {
            return [];
        }

        return settings.Cc.Count > 0
            ? [new TeamMail(settings.Cc, [], settings.Bcc)]
            : [.. settings.Bcc.Select(address => new TeamMail([address], [], []))];
    }

    private async Task<(List<string> Team, List<string> Visitors)> RecipientsAsync(EmailActionSettings settings, ActionRunContext context, bool quiet, CancellationToken cancellationToken)
    {
        var team = new List<string>(settings.To);
        if (EmailRecipientMap.Of(settings.RecipientMap, out _) is { } map && context.Scope.Answers.Find(map.Field) is { } chosen)
        {
            var values = chosen.Values.Select(value => value.Text).ToHashSet(StringComparer.Ordinal);
            team.AddRange(map.Routes.Where(route => values.Contains(route.Value)).SelectMany(route => route.To));
        }

        if (settings.ToCreator)
        {
            if (await CreatorAsync(context, cancellationToken) is { } creator)
            {
                team.Add(creator);
            }
            else if (!quiet)
            {
                LogNoCreator(logger, context.Scope.FormAlias);
            }
        }

        var visitors = new List<string>();
        if (settings.ToField is { } field && AnswerOf(context.Scope, field)?.Trim() is { } typed)
        {
            if (EmailAddresses.IsOne(typed))
            {
                visitors.Add(typed);
            }
            else if (!quiet)
            {
                LogNotAnAddress(logger, field);
            }
        }

        return (team, visitors);
    }

    private async Task<string?> CreatorAsync(ActionRunContext context, CancellationToken cancellationToken)
    {
        if (await forms.GetByAliasAsync(context.Scope.FormAlias, cancellationToken) is not { CreatedByUserKey: { } key })
        {
            return null;
        }

        var user = await users.GetAsync(key);
        return EmailAddresses.IsOne(user?.Email) ? user!.Email : null;
    }

    // A sensitive field shows only where the action names it (ADR-0014), in a header or an attachment too.
    private static bool MayShow(EmailActionSettings settings, MergeTagScope scope, Guid field) =>
        DefinitionKeys.Fields(scope.Form).FirstOrDefault(item => item.Key == field) is not { Sensitive: true } || settings.IncludeSensitive.Contains(field);

    private async Task<Guid[]> AttachmentsAsync(EmailActionSettings settings, ActionRunContext context, CancellationToken cancellationToken)
    {
        if (settings.Attachments.Count == 0 || context.EntryId is null)
        {
            return [];
        }

        var wanted = settings.Attachments.Where(field => MayShow(settings, context.Scope, field)).ToHashSet();
        var files = await entries.ListFilesAsync(context.EntryId.Value, cancellationToken);
        return [.. files.Where(file => wanted.Contains(file.FieldKey)).Select(file => file.Key)];
    }

    // The team's mails, with the copies, the reply-to and the files; none of those goes to an address a visitor typed.
    private async Task<IEnumerable<NewEmail>> TeamMailAsync(EmailActionSettings settings, ActionRunContext context, List<string> team, bool hasVisitor, CancellationToken cancellationToken)
    {
        var prepared = new List<NewEmail>();
        foreach (var each in TeamMails(settings, team, hasVisitor))
        {
            var mail = await MailAsync(settings, context, each.To, toVisitor: false, quiet: false, cancellationToken) with
            {
                Cc = each.Cc,
                Bcc = each.Bcc,
                ReplyTo = ReplyTo(settings, context.Scope),
                Attachments = await AttachmentsAsync(settings, context, cancellationToken),
            };
            if (await outbox.PrepareAsync(mail, cancellationToken) is { } ready)
            {
                prepared.Add(ready);
            }
        }

        return prepared;
    }

    private async Task<OutgoingEmail> MailAsync(EmailActionSettings settings, ActionRunContext context, IReadOnlyList<string> to, bool toVisitor, bool quiet, CancellationToken cancellationToken)
    {
        var scope = context.Scope with
        {
            IncludedSensitive = settings.IncludeSensitive.ToHashSet(),
            TableSkipsUnanswered = settings.WithoutUnanswered,
            BreaksVisitorLinks = toVisitor,
        };
        var subject = settings.Subject?.Resolve(scope.Culture) ?? string.Empty;
        var rendered = (await tags.RenderAsync(subject, MergeTagPlace.Mail, MergeTagContext.Header, scope, cancellationToken)).Text;
        return new OutgoingEmail
        {
            Kind = EmailKind.Action,
            To = to,
            From = settings.From,
            Subject = rendered,
            Body = await BodyAsync(settings, scope, rendered, quiet, cancellationToken),
            PlainText = settings.PlainText,
            Culture = scope.Culture,
            ToVisitor = toVisitor,
            Site = scope.Site,
            Layout = settings.Layout,
            EntryId = context.EntryId,
            ActionRunId = context.RunId,
        };
    }

    private async Task<string> BodyAsync(EmailActionSettings settings, MergeTagScope scope, string subject, bool quiet, CancellationToken cancellationToken)
    {
        if (settings.View is { } view)
        {
            var model = new EmailViewModel
            {
                Subject = subject,
                Culture = scope.Culture,
                FormName = scope.FormName,
                EntryNumber = scope.Entry?.Number ?? 0,
                Answers = (await tags.RenderAsync("{{ entry.answers }}", MergeTagPlace.Mail, MergeTagContext.Text, scope, cancellationToken)).Text,
            };
            foreach (var writer in views)
            {
                if (await writer.RenderAsync(view, model, cancellationToken) is { } written)
                {
                    return written;
                }
            }

            if (!quiet)
            {
                LogViewMissing(logger, view);
            }

            if (settings.Body is null)
            {
                throw new ViewMissingException($"The site has no mail view '{view}', and the action has no body of its own.");
            }
        }

        var body = settings.Body?.Resolve(scope.Culture) ?? string.Empty;
        return (await tags.RenderAsync(body, MergeTagPlace.Mail, settings.PlainText ? MergeTagContext.Text : MergeTagContext.Html, scope, cancellationToken)).Text;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Form {Form} has no creator with an address, so the mail to its creator was not queued")]
    private static partial void LogNoCreator(ILogger logger, string form);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The answer of field {Field} is not one address, so the mail to it was not queued")]
    private static partial void LogNotAnAddress(ILogger logger, Guid field);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The site has no mail view {View}; the action's body is used instead")]
    private static partial void LogViewMissing(ILogger logger, string view);

    // One of the team's mails: who it goes to and its copies.
    private sealed record TeamMail(IReadOnlyList<string> To, IReadOnlyList<string> Cc, IReadOnlyList<string> Bcc);
}
