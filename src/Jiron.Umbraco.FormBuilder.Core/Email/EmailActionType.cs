using System.Text.Json;
using System.Text.Json.Serialization;
using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Exchange;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Services;

namespace Jiron.Umbraco.FormBuilder.Core.Email;

/// <summary>The settings of the email action (ROADMAP 8.3, ADR-0160).</summary>
public sealed class EmailActionSettings
{
    /// <summary>Fixed addresses it goes to, usually the team's.</summary>
    [FormBuilderSetting("#jfbActionSettings_emailTo", Order = 1)]
    public IReadOnlyList<string> To { get; init; } = [];

    /// <summary>
    /// An email field whose answer it goes to. That address is one a visitor typed: it gets a mail of its own, which the limits
    /// of section 4.10 count, and links in what visitors typed are broken there.
    /// </summary>
    [FormBuilderSetting("#jfbActionSettings_emailToField", Order = 2, Kind = FieldSettingKind.Key, Editor = "fieldPicker")]
    public Guid? ToField { get; init; }

    /// <summary>Whether it goes to the user who created the form.</summary>
    [FormBuilderSetting("#jfbActionSettings_emailToCreator", Order = 3)]
    public bool ToCreator { get; init; }

    /// <summary>
    /// Addresses chosen by a choice field's answer, as <c>{"field": key, "routes": [{"value": "sales", "to": ["a@example.com"]}]}</c>.
    /// They stay on the server, as actions do (section 4.3.1).
    /// </summary>
    [FormBuilderSetting("#jfbActionSettings_emailRecipientMap", Order = 4, Editor = "recipientMap")]
    public JsonElement? RecipientMap { get; init; }

    /// <summary>Fixed addresses that get a copy.</summary>
    [FormBuilderSetting("#jfbActionSettings_emailCc", Order = 5)]
    public IReadOnlyList<string> Cc { get; init; } = [];

    /// <summary>Fixed addresses that get a blind copy.</summary>
    [FormBuilderSetting("#jfbActionSettings_emailBcc", Order = 6)]
    public IReadOnlyList<string> Bcc { get; init; } = [];

    /// <summary>An email field whose answer replies go to, when that answer is an address.</summary>
    [FormBuilderSetting("#jfbActionSettings_emailReplyTo", Order = 7, Kind = FieldSettingKind.Key, Editor = "fieldPicker")]
    public Guid? ReplyToField { get; init; }

    /// <summary>The address it comes from instead of the site's; a host usually locks it. Never a visitor's.</summary>
    [FormBuilderSetting("#jfbActionSettings_emailFrom", Order = 8, Group = FieldSettingGroup.Advanced)]
    public string? From { get; init; }

    /// <summary>The subject per culture, with merge tags.</summary>
    [FormBuilderSetting("#jfbActionSettings_emailSubject", Order = 9, SupportsMergeTags = true)]
    public LocalizedText? Subject { get; init; }

    /// <summary>The body per culture, with merge tags; <c>{{ entry.table }}</c> gives every answer as a table.</summary>
    [FormBuilderSetting("#jfbActionSettings_emailBody", Order = 10, SupportsMergeTags = true)]
    public LocalizedHtml? Body { get; init; }

    /// <summary>Whether <c>{{ entry.table }}</c> leaves out the fields without an answer.</summary>
    [FormBuilderSetting("#jfbActionSettings_emailWithoutUnanswered", Order = 11)]
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
    [FormBuilderSetting("#jfbActionSettings_emailIncludeSensitive", Order = 15, Kind = FieldSettingKind.Key, Editor = "fieldPicker", Group = FieldSettingGroup.Advanced)]
    public IReadOnlyList<Guid> IncludeSensitive { get; init; } = [];

    /// <summary>The upload fields whose files go along; none by default.</summary>
    [FormBuilderSetting("#jfbActionSettings_emailAttachments", Order = 16, Kind = FieldSettingKind.Key, Editor = "fieldPicker", Group = FieldSettingGroup.Advanced)]
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

            var (team, visitors) = await RecipientsAsync(settings, context, cancellationToken);
            if (team.Count > 0 || visitors.Count > 0)
            {
                prepared.AddRange(await TeamMailAsync(settings, context, team, visitors.Count > 0, cancellationToken));
                foreach (var visitor in visitors)
                {
                    var mail = await MailAsync(settings, context, [visitor], toVisitor: true, cancellationToken);
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
            return new ActionTestResult(ActionTestOutcome.Failed, "Your account has no address a test mail can go to.");
        }

        var run = new ActionRunContext { Action = context.Action, Trigger = ActionTrigger.Submitted, Scope = context.Scope, Attempt = 1, DeliveryKey = string.Empty };
        OutgoingEmail mail;
        try
        {
            mail = await MailAsync(settings, run, [context.User.Email!], toVisitor: false, cancellationToken);
        }
        catch (ViewMissingException exception)
        {
            return new ActionTestResult(ActionTestOutcome.Failed, exception.Message);
        }

        var stored = await outbox.QueueAsync(mail with { Kind = EmailKind.Test, EntryId = null, ActionRunId = null, Cc = [], Bcc = [], Attachments = [] }, cancellationToken);
        return stored is null
            ? new ActionTestResult(ActionTestOutcome.Failed, "The test mail could not be queued.")
            : new ActionTestResult(ActionTestOutcome.Sent, $"Sent to {context.User.Email}", [stored.Subject, stored.TextBody]);
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
            foreach (var problem in tags.Check(culture.Text, MergeTagPlace.Mail, context, check.Definition).Where(problem => !problem.IsWarning))
            {
                check.Invalid(setting, $"A merge tag at {problem.At} cannot be used here: {problem.Code}.");
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

    private async Task<(List<string> Team, List<string> Visitors)> RecipientsAsync(EmailActionSettings settings, ActionRunContext context, CancellationToken cancellationToken)
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
            else
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
            else
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

    // The team's one mail, with the copies, the reply-to and the files; none of those goes to an address a visitor typed. Copies on a
    // mail that has only such an address go as a mail of their own.
    private async Task<IEnumerable<NewEmail>> TeamMailAsync(EmailActionSettings settings, ActionRunContext context, List<string> team, bool hasVisitor, CancellationToken cancellationToken)
    {
        var cc = settings.Cc;
        var bcc = settings.Bcc;
        if (team.Count == 0 && hasVisitor && (cc.Count > 0 || bcc.Count > 0))
        {
            team = [.. cc, .. bcc];
            (cc, bcc) = ([], []);
        }

        if (team.Count == 0)
        {
            return [];
        }

        var mail = await MailAsync(settings, context, team, toVisitor: false, cancellationToken) with
        {
            Cc = cc,
            Bcc = bcc,
            ReplyTo = settings.ReplyToField is { } field && MayShow(settings, context.Scope, field) ? AnswerOf(context.Scope, field)?.Trim() : null,
            Attachments = await AttachmentsAsync(settings, context, cancellationToken),
        };
        return await outbox.PrepareAsync(mail, cancellationToken) is { } ready ? [ready] : [];
    }

    private async Task<OutgoingEmail> MailAsync(EmailActionSettings settings, ActionRunContext context, IReadOnlyList<string> to, bool toVisitor, CancellationToken cancellationToken)
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
            Body = await BodyAsync(settings, scope, rendered, cancellationToken),
            PlainText = settings.PlainText,
            Culture = scope.Culture,
            ToVisitor = toVisitor,
            Site = scope.Site,
            Layout = settings.Layout,
            EntryId = context.EntryId,
            ActionRunId = context.RunId,
        };
    }

    private async Task<string> BodyAsync(EmailActionSettings settings, MergeTagScope scope, string subject, CancellationToken cancellationToken)
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

            LogViewMissing(logger, view);
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
}
