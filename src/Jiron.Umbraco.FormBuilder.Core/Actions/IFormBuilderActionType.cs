using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;

namespace Jiron.Umbraco.FormBuilder.Core.Actions;

/// <summary>How a test run of an action type reaches anyone (section 4.4's test runs, ADR-0158).</summary>
public enum ActionTestKind
{
    /// <summary>It creates nothing and sends nothing: it shows what a run would do, as content actions do.</summary>
    DryRun,

    /// <summary>It reaches only the user who asks for it, as an email does.</summary>
    ToCurrentUser,

    /// <summary>It reaches what the action is set up to reach, so the user confirms it first, as a webhook does.</summary>
    AfterConfirmation,
}

/// <summary>What an action type is (section 4.9, ADR-0158): its alias, how the designer shows it and how a test run of it behaves.</summary>
public sealed record ActionTypeDeclaration
{
    /// <summary>
    /// The alias forms name the type by, for example <c>email</c>: a letter, then letters, digits, '.', '-' and '_', at
    /// most 64 characters. A type of a site or an add-on starts with its own prefix, such as <c>acme.crm</c>, so that it
    /// never meets a later built-in type.
    /// </summary>
    public required string Alias { get; init; }

    /// <summary>The name the designer shows: plain text, or a key of the backoffice's localisation that starts with <c>#</c>.</summary>
    public required string Name { get; init; }

    /// <summary>The icon the designer shows, one of Umbraco's backoffice icons, for example <c>icon-message</c>.</summary>
    public required string Icon { get; init; }

    /// <summary>How a test run of the type behaves; by default it reaches nobody.</summary>
    public ActionTestKind Test { get; init; } = ActionTestKind.DryRun;

    /// <summary>
    /// How long one attempt of the type may take, at most one minute (ADR-0007): null for that minute. An attempt past it
    /// is cut off, and the run becomes uncertain, because it may have been delivered.
    /// </summary>
    public TimeSpan? AttemptTimeout { get; init; }
}

/// <summary>
/// A kind of action that runs after a submission (section 4.9, ADR-0158): a declaration, a settings schema with its own
/// check, what it needs of the site's configuration, a run and a test run. Register it with
/// <c>builder.JironFormBuilder().ActionTypes().Append&lt;MyActionType&gt;()</c>. It is a singleton, and runs from several
/// attempts at once. <see cref="FormBuilderActionTypeBase{TSettings}"/> is the usual way to write one.
/// </summary>
public interface IFormBuilderActionType
{
    /// <summary>What the type is.</summary>
    ActionTypeDeclaration Declaration { get; }

    /// <summary>The type's settings, which the designer edits and publishing holds an action to.</summary>
    FieldSettingsSchema SettingsSchema { get; }

    /// <summary>
    /// The type's own check of an action's settings: publishing runs it once they keep the schema and the rules of its
    /// attributes, so they read as the type's settings class.
    /// </summary>
    /// <param name="check">The action and where its problems go.</param>
    void CheckSettings(ActionSettingsCheck check);

    /// <summary>
    /// What this site lacks for the action, each named by its configuration key and never a value, such as
    /// <c>JironFormBuilder:Secrets:Slack</c>: an import switches off an action that lacks something, with a note that names
    /// it (ADR-0046). Empty when the site has all it needs.
    /// </summary>
    /// <param name="action">The action, with its settings.</param>
    IReadOnlyList<string> MissingConfiguration(FormAction action);

    /// <summary>
    /// Runs one attempt for an entry. An exception counts as <see cref="ActionResult.TryAgain"/>, and is recorded by its
    /// type alone, as its message may hold what a visitor typed or a secret. A cancellation through the token counts as
    /// cut off: the run becomes uncertain, because the attempt may have been delivered.
    /// </summary>
    /// <param name="context">The entry and the action.</param>
    /// <param name="cancellationToken">Cancelled when the attempt's time is up, or when the host stops.</param>
    Task<ActionResult> RunAsync(ActionRunContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Runs the action against a sample entry for the designer, as <see cref="ActionTypeDeclaration.Test"/> says: no entry
    /// and no run changes. An email goes to the current user only, a webhook carries <c>X-Jfb-Test: true</c>, and content
    /// actions create nothing.
    /// </summary>
    /// <param name="context">The sample entry, the action and the user.</param>
    /// <param name="cancellationToken">Cancelled when the test's time is up, or when the request ends.</param>
    Task<ActionTestResult> TestAsync(ActionTestContext context, CancellationToken cancellationToken);
}
