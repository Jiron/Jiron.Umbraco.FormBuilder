using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Submissions;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Entries;

/// <summary>One entry as the entry panel shows it (ADR-0208).</summary>
public sealed class EntryPanelResponseModel
{
    /// <summary>The entry.</summary>
    public required Guid Key { get; init; }

    /// <summary>Its form.</summary>
    public required Guid Form { get; init; }

    /// <summary>Its form's name.</summary>
    public required string FormName { get; init; }

    /// <summary>Its number.</summary>
    public required int Number { get; init; }

    /// <summary>Its entry title, or none.</summary>
    public required string? Title { get; init; }

    /// <summary>Its state: <c>unverified</c>, <c>submitted</c>, <c>approved</c>, <c>rejected</c> or <c>spam</c>.</summary>
    public required string State { get; init; }

    /// <summary>When it was sent.</summary>
    public required DateTimeOffset Received { get; init; }

    /// <summary>Whether its version waits for a person to approve its entries.</summary>
    public required bool ManualApproval { get; init; }

    /// <summary>Whether it is a test entry.</summary>
    public required bool IsTest { get; init; }

    /// <summary>Where and how it was sent.</summary>
    public required EntryPanelDetailsResponseModel Details { get; init; }

    /// <summary>Its answers, by the pages and groups of its version.</summary>
    public required IReadOnlyList<EntryPanelPageResponseModel> Pages { get; init; }

    /// <summary>How many notes it has.</summary>
    public required int Notes { get; init; }

    /// <summary>The end of its legal hold, while one keeps it (ADR-0222).</summary>
    public DateTimeOffset? HeldUntil { get; init; }

    /// <summary>What the user may do on its form, by name.</summary>
    public required IReadOnlyList<string> Permissions { get; init; }
}

/// <summary>Where and how an entry was sent.</summary>
public sealed class EntryPanelDetailsResponseModel
{
    /// <summary>The address of the page it was sent from, or none.</summary>
    public required string? Page { get; init; }

    /// <summary>The language it was sent in.</summary>
    public required string Culture { get; init; }

    /// <summary>How many seconds it took to fill in, when known.</summary>
    public required int? Duration { get; init; }

    /// <summary>The member who sent it, or none.</summary>
    public required Guid? Member { get; init; }

    /// <summary>That member's name, while the member exists.</summary>
    public required string? MemberName { get; init; }

    /// <summary>The address it was sent from, as the form kept it, or none.</summary>
    public required string? IpAddress { get; init; }

    /// <summary>What the spam protection found, as codes.</summary>
    public required IReadOnlyList<string> SpamReasons { get; init; }
}

/// <summary>A page of an entry's version, with its groups the rules showed.</summary>
public sealed class EntryPanelPageResponseModel
{
    /// <summary>The page.</summary>
    public required Guid Key { get; init; }

    /// <summary>Its title, or none.</summary>
    public required string? Title { get; init; }

    /// <summary>Its groups.</summary>
    public required IReadOnlyList<EntryPanelGroupResponseModel> Groups { get; init; }
}

/// <summary>A group of an entry's version, with the answers of its fields the rules showed.</summary>
public sealed class EntryPanelGroupResponseModel
{
    /// <summary>The group.</summary>
    public required Guid Key { get; init; }

    /// <summary>Its title, or none.</summary>
    public required string? Title { get; init; }

    /// <summary>Its fields' answers.</summary>
    public required IReadOnlyList<EntryPanelAnswerResponseModel> Answers { get; init; }
}

/// <summary>A field's answer in the panel.</summary>
public sealed class EntryPanelAnswerResponseModel
{
    /// <summary>The field.</summary>
    public required Guid Field { get; init; }

    /// <summary>Its label.</summary>
    public required string Label { get; init; }

    /// <summary>Whether the form's live version no longer has it.</summary>
    public required bool Removed { get; init; }

    /// <summary>Whether it is sensitive.</summary>
    public required bool Sensitive { get; init; }

    /// <summary>Whether its answer is held back until revealed.</summary>
    public required bool Masked { get; init; }

    /// <summary>Whether its answer was kept only until the actions ran, and is gone.</summary>
    public required bool NotKept { get; init; }

    /// <summary>Its answer's texts: one for each answered row of a repeatable group; none when masked or unanswered.</summary>
    public required IReadOnlyList<string> Texts { get; init; }

    /// <summary>Its uploaded files.</summary>
    public required IReadOnlyList<EntryPanelFileResponseModel> Files { get; init; }

    /// <summary>The exact text the visitor agreed to, for a consent.</summary>
    public required string? Consent { get; init; }

    /// <summary>The name a typed signature was signed with.</summary>
    public required string? SignedName { get; init; }

    /// <summary>How the panel edits it, for a user who may edit entries; none otherwise.</summary>
    public required EntryPanelEditResponseModel? Edit { get; init; }
}

/// <summary>An uploaded file of an answer.</summary>
public sealed class EntryPanelFileResponseModel
{
    /// <summary>The file, which its authorised download names.</summary>
    public required Guid Key { get; init; }

    /// <summary>The name it was uploaded with.</summary>
    public required string Name { get; init; }

    /// <summary>Its media type.</summary>
    public required string ContentType { get; init; }

    /// <summary>Its size in bytes.</summary>
    public required long Size { get; init; }

    /// <summary>Whether the panel shows it in its viewer.</summary>
    public required bool Image { get; init; }

    /// <summary>Whether it is held back, quarantined or waiting for its scan.</summary>
    public required bool Held { get; init; }
}

/// <summary>How the panel edits an answer.</summary>
public sealed class EntryPanelEditResponseModel
{
    /// <summary><c>text</c>, <c>longText</c>, <c>number</c>, <c>date</c>, <c>choice</c>, <c>choices</c> or <c>checkbox</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>The answer's values now, in the wire form.</summary>
    public required IReadOnlyList<string> Values { get; init; }

    /// <summary>The field's own options, for a choice.</summary>
    public required IReadOnlyList<EntryListOptionResponseModel> Options { get; init; }

    /// <summary>Whether the field needs an answer.</summary>
    public required bool Required { get; init; }
}

/// <summary>Reveal one answer.</summary>
public sealed class EntryRevealRequestModel
{
    /// <summary>A field of a group of the entry's version, which may be a repeatable group.</summary>
    public Guid Field { get; init; }
}

/// <summary>An entry's events and mail, newest first.</summary>
public sealed class EntryTimelineResponseModel
{
    /// <summary>Its events.</summary>
    public required IReadOnlyList<EntryTimelineEventResponseModel> Events { get; init; }

    /// <summary>Its action runs.</summary>
    public required IReadOnlyList<EntryTimelineRunResponseModel> Runs { get; init; }

    /// <summary>Its mail.</summary>
    public required IReadOnlyList<EntryTimelineMailResponseModel> Mail { get; init; }
}

/// <summary>An action run of an entry.</summary>
public sealed class EntryTimelineRunResponseModel
{
    /// <summary>The run, which its retry names.</summary>
    public required long Id { get; init; }

    /// <summary>When it last tried, finished or was queued.</summary>
    public required DateTimeOffset When { get; init; }

    /// <summary>The action.</summary>
    public required Guid Action { get; init; }

    /// <summary>The action's name, or none.</summary>
    public required string? Name { get; init; }

    /// <summary>The action type's alias.</summary>
    public required string Type { get; init; }

    /// <summary>Where it stands: <c>pending</c>, <c>running</c>, <c>succeeded</c>, <c>failed</c>, <c>skipped</c> or <c>uncertain</c>.</summary>
    public required string State { get; init; }

    /// <summary>How many times it tried.</summary>
    public required int Attempts { get; init; }

    /// <summary>Why its last try failed, or none.</summary>
    public required string? Error { get; init; }
}

/// <summary>An event of an entry's timeline.</summary>
public sealed class EntryTimelineEventResponseModel
{
    /// <summary>The event.</summary>
    public required long Id { get; init; }

    /// <summary>When it happened.</summary>
    public required DateTimeOffset When { get; init; }

    /// <summary>What happened, such as <c>submitted</c>, <c>stateChanged</c>, <c>opened</c> or <c>answersEdited</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>The name of the backoffice user who did it, or none.</summary>
    public required string? User { get; init; }

    /// <summary>The state a move or an Undo left.</summary>
    public required string? From { get; init; }

    /// <summary>The state it reached.</summary>
    public required string? To { get; init; }

    /// <summary>How many actions a re-run queued.</summary>
    public required int? Count { get; init; }

    /// <summary>Whom a forward went to.</summary>
    public required string? Recipient { get; init; }

    /// <summary>When a legal hold it set ends.</summary>
    public DateTimeOffset? Until { get; init; }

    /// <summary>What an edit changed.</summary>
    public required IReadOnlyList<EntryTimelineChangeResponseModel> Changes { get; init; }
}

/// <summary>A field an edit changed.</summary>
public sealed class EntryTimelineChangeResponseModel
{
    /// <summary>Its label.</summary>
    public required string Label { get; init; }

    /// <summary>Its answer's texts before.</summary>
    public required IReadOnlyList<string> Before { get; init; }

    /// <summary>Its answer's texts after.</summary>
    public required IReadOnlyList<string> After { get; init; }

    /// <summary>Whether the reader may not see them.</summary>
    public required bool Masked { get; init; }
}

/// <summary>A mail of an entry.</summary>
public sealed class EntryTimelineMailResponseModel
{
    /// <summary>The mail.</summary>
    public required long Id { get; init; }

    /// <summary>When it went out, or was queued.</summary>
    public required DateTimeOffset When { get; init; }

    /// <summary>What sent it, such as <c>action</c>, <c>forward</c> or <c>verification</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>Its recipients.</summary>
    public required string To { get; init; }

    /// <summary>Its subject.</summary>
    public required string Subject { get; init; }

    /// <summary>Where it stands: <c>queued</c>, <c>sending</c>, <c>sent</c>, <c>failed</c> or another state.</summary>
    public required string State { get; init; }

    /// <summary>The action run that sent it, or none.</summary>
    public required long? ActionRun { get; init; }
}

/// <summary>A note on an entry.</summary>
public sealed class EntryNoteResponseModel
{
    /// <summary>The note.</summary>
    public required int Id { get; init; }

    /// <summary>Its author's name.</summary>
    public required string Author { get; init; }

    /// <summary>Whether the user wrote it, and so may change it.</summary>
    public required bool Mine { get; init; }

    /// <summary>What it says.</summary>
    public required string Body { get; init; }

    /// <summary>When it was written.</summary>
    public required DateTimeOffset Created { get; init; }

    /// <summary>When it was last changed, or none.</summary>
    public required DateTimeOffset? Edited { get; init; }
}

/// <summary>Write or change a note.</summary>
public sealed class EntryNoteRequestModel
{
    /// <summary>What it says: 1 to 10,000 characters.</summary>
    public string Body { get; init; } = string.Empty;
}

/// <summary>Edit an entry's answers.</summary>
public sealed class EntryEditRequestModel
{
    /// <summary>The new answers, each field once.</summary>
    public IReadOnlyList<EntryEditAnswerRequestModel> Answers { get; init; } = [];

    /// <summary>Whether the user agreed to overbook options and double unique answers.</summary>
    public bool Overbook { get; init; }
}

/// <summary>A field's new answer.</summary>
public sealed class EntryEditAnswerRequestModel
{
    /// <summary>The field.</summary>
    public Guid Field { get; init; }

    /// <summary>Its values in the wire form; none clears it.</summary>
    public IReadOnlyList<string> Values { get; init; } = [];
}

/// <summary>What an edit did, or what it asks first.</summary>
public sealed class EntryEditResponseModel
{
    /// <summary>How many fields it changed; none while it asks.</summary>
    public required int Changed { get; init; }

    /// <summary>What it would overbook or double, which the user must agree to; then nothing changed.</summary>
    public required EntryMoveAsksResponseModel? Asks { get; init; }
}

/// <summary>What a move back or an edit would overbook or double (ADR-0208).</summary>
public sealed class EntryMoveAsksResponseModel
{
    /// <summary>The options past their places.</summary>
    public required IReadOnlyList<EntryFullOptionResponseModel> Full { get; init; }

    /// <summary>The answers that must be unique and another entry holds.</summary>
    public required IReadOnlyList<EntryHeldAnswerResponseModel> Held { get; init; }
}

/// <summary>An option past its places.</summary>
public sealed class EntryFullOptionResponseModel
{
    /// <summary>The field.</summary>
    public required Guid Field { get; init; }

    /// <summary>Its label.</summary>
    public required string FieldLabel { get; init; }

    /// <summary>The option's value.</summary>
    public required string Option { get; init; }

    /// <summary>Its label.</summary>
    public required string OptionLabel { get; init; }

    /// <summary>How many places it has.</summary>
    public required int Places { get; init; }

    /// <summary>How many the other entries took.</summary>
    public required int Taken { get; init; }
}

/// <summary>An answer another entry holds.</summary>
public sealed class EntryHeldAnswerResponseModel
{
    /// <summary>The field.</summary>
    public required Guid Field { get; init; }

    /// <summary>Its label.</summary>
    public required string FieldLabel { get; init; }
}

/// <summary>Whom an entry may be forwarded to.</summary>
public sealed class EntryForwardRecipientsResponseModel
{
    /// <summary>The users who may view the form's entries, by name.</summary>
    public required IReadOnlyList<EntryForwardUserResponseModel> Users { get; init; }

    /// <summary>The forward addresses of the entry's site.</summary>
    public required IReadOnlyList<string> Addresses { get; init; }
}

/// <summary>A user an entry may be forwarded to.</summary>
public sealed class EntryForwardUserResponseModel
{
    /// <summary>The user.</summary>
    public required Guid Key { get; init; }

    /// <summary>Their name.</summary>
    public required string Name { get; init; }
}

/// <summary>Forward an entry to a user or an address.</summary>
public sealed class EntryForwardRequestModel
{
    /// <summary>The user it goes to; or none, for an address.</summary>
    public Guid? User { get; init; }

    /// <summary>The forward address it goes to, when no user.</summary>
    public string? Address { get; init; }

    /// <summary>A message, at most 2,000 characters.</summary>
    public string? Message { get; init; }

    /// <summary>Whether sensitive values go along; only for a user who may see them.</summary>
    public bool IncludeSensitive { get; init; }
}

/// <summary>The form an entry belongs to.</summary>
public sealed class EntryLocationResponseModel
{
    /// <summary>The form; none for an entry that is gone, a draft, or one the user may not view.</summary>
    public required Guid? Form { get; init; }
}

/// <summary>Maps the panel's records to its models.</summary>
internal static class EntryPanelMapping
{
    /// <summary>The model of an answer.</summary>
    public static EntryPanelAnswerResponseModel Answer(EntryPanelAnswer answer) => new()
    {
        Field = answer.Field,
        Label = answer.Label,
        Removed = answer.Removed,
        Sensitive = answer.Sensitive,
        Masked = answer.Masked,
        NotKept = answer.NotKept,
        Texts = answer.Texts,
        Files = [.. answer.Files.Select(file => new EntryPanelFileResponseModel { Key = file.Key, Name = file.Name, ContentType = file.ContentType, Size = file.Size, Image = file.Image, Held = file.Held })],
        Consent = answer.Consent,
        SignedName = answer.SignedName,
        Edit = answer.Edit is { } edit
            ? new EntryPanelEditResponseModel
            {
                Kind = edit.Kind,
                Values = edit.Values,
                Options = [.. edit.Options.Select(option => new EntryListOptionResponseModel { Value = option.Value, Label = option.Label })],
                Required = edit.Required,
            }
            : null,
    };

    /// <summary>The model of an entry.</summary>
    public static EntryPanelResponseModel Panel(EntryPanel panel, IReadOnlyList<string> permissions) => new()
    {
        Key = panel.Key,
        Form = panel.FormKey,
        FormName = panel.FormName,
        Number = panel.Number,
        Title = panel.Title,
        State = StoredNames.Of(panel.State),
        Received = panel.Received,
        ManualApproval = panel.ManualApproval,
        IsTest = panel.IsTest,
        Details = new EntryPanelDetailsResponseModel
        {
            Page = panel.Details.Page,
            Culture = panel.Details.Culture,
            Duration = panel.Details.Duration,
            Member = panel.Details.Member,
            MemberName = panel.Details.MemberName,
            IpAddress = panel.Details.IpAddress,
            SpamReasons = panel.Details.SpamReasons,
        },
        Pages = [.. panel.Pages.Select(page => new EntryPanelPageResponseModel
        {
            Key = page.Key,
            Title = page.Title,
            Groups = [.. page.Groups.Select(group => new EntryPanelGroupResponseModel { Key = group.Key, Title = group.Title, Answers = [.. group.Answers.Select(Answer)] })],
        })],
        Notes = panel.Notes,
        HeldUntil = panel.HeldUntil,
        Permissions = permissions,
    };

    /// <summary>The model of what a move back or an edit asks first.</summary>
    public static EntryMoveAsksResponseModel Asks(EntryMoveCheck asks) => new()
    {
        Full = [.. asks.Full.Select(full => new EntryFullOptionResponseModel { Field = full.Field, FieldLabel = full.FieldLabel, Option = full.Option, OptionLabel = full.OptionLabel, Places = full.Places, Taken = full.Taken })],
        Held = [.. asks.Held.Select(held => new EntryHeldAnswerResponseModel { Field = held.Field, FieldLabel = held.FieldLabel })],
    };
}
