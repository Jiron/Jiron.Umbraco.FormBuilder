using System.Text.Json.Serialization;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;

namespace Jiron.Umbraco.FormBuilder.Core.Definitions;

// The fixed words of a definition (ADR-0002). In JSON each is its camelCase name, never a number; a new word needs a
// new schemaVersion, so an older site never reads a newer document without noticing.

/// <summary>How a group frames its fields.</summary>
[JsonConverter(typeof(DefinitionEnumConverter<GroupStyle>))]
public enum GroupStyle
{
    /// <summary>No frame (<c>plain</c>, the default).</summary>
    Plain,

    /// <summary>A card around the group (<c>card</c>).</summary>
    Card,
}

/// <summary>How long a field's value is kept in the entry (section 4.4).</summary>
[JsonConverter(typeof(DefinitionEnumConverter<FieldKeep>))]
public enum FieldKeep
{
    /// <summary>As long as the entry (<c>always</c>, the default).</summary>
    Always,

    /// <summary>Only until the entry's actions have run (<c>untilActionsRun</c>).</summary>
    UntilActionsRun,
}

/// <summary>Whether a value prefilled from the member is shown to them.</summary>
[JsonConverter(typeof(DefinitionEnumConverter<PrefillMemberMode>))]
public enum PrefillMemberMode
{
    /// <summary>The field shows the member's value and can be changed (<c>show</c>, the default).</summary>
    Show,

    /// <summary>The field is not shown; the value is taken from the member when the entry is submitted (<c>hide</c>).</summary>
    Hide,
}

/// <summary>Where a choice field's options come from.</summary>
[JsonConverter(typeof(DefinitionEnumConverter<OptionSource>))]
public enum OptionSource
{
    /// <summary>The field's own items (<c>items</c>, the default).</summary>
    Items,

    /// <summary>An option list of the library (<c>list</c>).</summary>
    List,

    /// <summary>Content items (<c>content</c>).</summary>
    Content,

    /// <summary>The values of an Umbraco data type (<c>dataType</c>).</summary>
    DataType,

    /// <summary>A JSON document at a URL (<c>url</c>).</summary>
    Url,

    /// <summary>A CSV or text file (<c>file</c>).</summary>
    File,

    /// <summary>The countries (<c>countries</c>).</summary>
    Countries,

    /// <summary>A developer's option source (<c>code</c>).</summary>
    Code,
}

/// <summary>What a rule does while its conditions match.</summary>
[JsonConverter(typeof(DefinitionEnumConverter<RuleEffect>))]
public enum RuleEffect
{
    /// <summary>Shows the page, group or field (<c>show</c>); it is hidden otherwise.</summary>
    Show,

    /// <summary>Hides the page, group or field (<c>hide</c>).</summary>
    Hide,

    /// <summary>Makes the field required (<c>require</c>).</summary>
    Require,

    /// <summary>Lets the action run (<c>run</c>).</summary>
    Run,

    /// <summary>Uses the outcome (<c>use</c>).</summary>
    Use,
}

/// <summary>Whether all conditions must match or one is enough.</summary>
[JsonConverter(typeof(DefinitionEnumConverter<RuleMatch>))]
public enum RuleMatch
{
    /// <summary>Every condition matches (<c>all</c>, the default); no conditions match.</summary>
    All,

    /// <summary>At least one condition matches (<c>any</c>); no conditions do not match.</summary>
    Any,
}

/// <summary>What starts an action.</summary>
[JsonConverter(typeof(DefinitionEnumConverter<ActionTrigger>))]
public enum ActionTrigger
{
    /// <summary>The entry was submitted (<c>submitted</c>, the default).</summary>
    Submitted,

    /// <summary>The entry was approved (<c>approved</c>).</summary>
    Approved,

    /// <summary>The entry was rejected (<c>rejected</c>).</summary>
    Rejected,

    /// <summary>The visitor confirmed their email address (<c>verified</c>).</summary>
    Verified,
}

/// <summary>What the visitor gets after sending.</summary>
[JsonConverter(typeof(DefinitionEnumConverter<OutcomeKind>))]
public enum OutcomeKind
{
    /// <summary>A message, optionally followed by a redirect (<c>message</c>, the default).</summary>
    Message,

    /// <summary>A redirect (<c>redirect</c>).</summary>
    Redirect,
}

/// <summary>How a form with several pages shows the visitor's progress.</summary>
[JsonConverter(typeof(DefinitionEnumConverter<ProgressIndicator>))]
public enum ProgressIndicator
{
    /// <summary>Numbered steps (<c>steps</c>, the default).</summary>
    Steps,

    /// <summary>A progress bar (<c>bar</c>).</summary>
    Bar,

    /// <summary>Nothing (<c>none</c>).</summary>
    None,
}

/// <summary>Whether entries are stored.</summary>
[JsonConverter(typeof(DefinitionEnumConverter<EntryStorage>))]
public enum EntryStorage
{
    /// <summary>Entries are stored (<c>always</c>, the default).</summary>
    Always,

    /// <summary>Entries are kept only until their actions are final (<c>never</c>).</summary>
    Never,

    /// <summary>Entries are kept for the number of days in <c>storeDays</c> (<c>days</c>).</summary>
    Days,
}

/// <summary>Whether entries wait for a person's approval.</summary>
[JsonConverter(typeof(DefinitionEnumConverter<ApprovalMode>))]
public enum ApprovalMode
{
    /// <summary>Entries are approved at once (<c>automatic</c>, the default).</summary>
    Automatic,

    /// <summary>Entries wait for approval in the backoffice (<c>manual</c>).</summary>
    Manual,
}

/// <summary>Who may fill in the form.</summary>
[JsonConverter(typeof(DefinitionEnumConverter<MemberAccess>))]
public enum MemberAccess
{
    /// <summary>Every visitor (<c>anyone</c>, the default).</summary>
    Anyone,

    /// <summary>Signed-in members (<c>members</c>).</summary>
    Members,

    /// <summary>Members of the groups in <c>memberGroups</c> (<c>groups</c>).</summary>
    Groups,
}

/// <summary>The form's spam protection (ADR-0012).</summary>
[JsonConverter(typeof(DefinitionEnumConverter<ProtectionLevel>))]
public enum ProtectionLevel
{
    /// <summary>The site's default (<c>inherit</c>, the default).</summary>
    Inherit,

    /// <summary>The standard protection (<c>standard</c>).</summary>
    Standard,

    /// <summary>The standard protection and a proof of work (<c>proofOfWork</c>).</summary>
    ProofOfWork,

    /// <summary>A captcha provider (<c>captcha</c>).</summary>
    Captcha,
}

/// <summary>How strictly the content rules judge an entry (ADR-0012).</summary>
[JsonConverter(typeof(DefinitionEnumConverter<ContentRuleLevel>))]
public enum ContentRuleLevel
{
    /// <summary>The site's default (<c>inherit</c>, the default).</summary>
    Inherit,

    /// <summary>No content rules (<c>off</c>).</summary>
    Off,

    /// <summary>The normal rules (<c>normal</c>).</summary>
    Normal,

    /// <summary>The strict rules (<c>strict</c>).</summary>
    Strict,
}

/// <summary>What an entry keeps of the visitor's IP address (ADR-0009).</summary>
[JsonConverter(typeof(DefinitionEnumConverter<IpHandling>))]
public enum IpHandling
{
    /// <summary>The site's default (<c>inherit</c>, the default).</summary>
    Inherit,

    /// <summary>Nothing (<c>none</c>).</summary>
    None,

    /// <summary>The address with its last part removed (<c>anonymised</c>).</summary>
    Anonymised,

    /// <summary>The full address, where the host allows it (<c>full</c>).</summary>
    Full,
}

/// <summary>How required and optional fields are marked.</summary>
[JsonConverter(typeof(DefinitionEnumConverter<RequiredMarker>))]
public enum RequiredMarker
{
    /// <summary>The site's default (<c>inherit</c>, the default).</summary>
    Inherit,

    /// <summary>Required fields carry an asterisk (<c>required</c>).</summary>
    Required,

    /// <summary>Optional fields say "(optional)" (<c>optional</c>).</summary>
    Optional,

    /// <summary>No marker (<c>none</c>).</summary>
    None,
}

/// <summary>How choice fields lay out their options unless a field says otherwise.</summary>
[JsonConverter(typeof(DefinitionEnumConverter<ChoiceLayout>))]
public enum ChoiceLayout
{
    /// <summary>One option per line (<c>vertical</c>, the default).</summary>
    Vertical,

    /// <summary>Options side by side (<c>horizontal</c>).</summary>
    Horizontal,

    /// <summary>A card per option, with its image when it has one (<c>cards</c>).</summary>
    Cards,
}
