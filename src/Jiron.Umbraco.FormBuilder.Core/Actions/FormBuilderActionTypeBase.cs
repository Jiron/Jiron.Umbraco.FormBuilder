using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;

namespace Jiron.Umbraco.FormBuilder.Core.Actions;

/// <summary>
/// The usual way to write an action type (section 4.9, ADR-0158): a declaration and a settings class, whose properties
/// with <see cref="FormBuilderSettingAttribute"/> become the type's settings schema, as a field type's do.
/// </summary>
/// <typeparam name="TSettings">The type's settings class; <see cref="NoFieldSettings"/> for a type without settings.</typeparam>
public abstract class FormBuilderActionTypeBase<TSettings> : IFormBuilderActionType
    where TSettings : class, new()
{
    private readonly JsonTypeInfo<TSettings> _settingsInfo;

    /// <summary>A type with its declaration, and the schema of <typeparamref name="TSettings"/>.</summary>
    /// <param name="declaration">What the type is.</param>
    /// <exception cref="InvalidOperationException"><typeparamref name="TSettings"/> cannot be a settings class; the message names the type and every reason.</exception>
    protected FormBuilderActionTypeBase(ActionTypeDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        Declaration = declaration;
        try
        {
            SettingsSchema = FieldSettingsReader.Read(typeof(TSettings));
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException($"The settings of the action type '{declaration.Alias}' ({GetType().FullName}) cannot be read: {exception.Message}", exception);
        }

        _settingsInfo = (JsonTypeInfo<TSettings>)FieldSettingsJson.Options.GetTypeInfo(typeof(TSettings));
    }

    /// <inheritdoc />
    public ActionTypeDeclaration Declaration { get; }

    /// <inheritdoc />
    public FieldSettingsSchema SettingsSchema { get; }

    /// <summary>An action's settings as <typeparamref name="TSettings"/>; a new instance, with its defaults, for none.</summary>
    /// <param name="settings">The action's settings, which keep the type's schema.</param>
    /// <exception cref="JsonException">The settings do not fit the class; publishing holds them to the schema first.</exception>
    public TSettings ReadSettings(SettingsObject? settings) => settings?.Bind(_settingsInfo) ?? new TSettings();

    /// <inheritdoc />
    /// <remarks>None unless a type overrides it.</remarks>
    public virtual ValueTask<IReadOnlyDictionary<string, IReadOnlyList<FieldSettingChoice>>> SiteChoicesAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyDictionary<string, IReadOnlyList<FieldSettingChoice>>>(new Dictionary<string, IReadOnlyList<FieldSettingChoice>>(StringComparer.Ordinal));

    /// <inheritdoc />
    public void CheckSettings(ActionSettingsCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);
        CheckSettings(ReadSettings(check.Action.Settings), check);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> MissingConfiguration(FormAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return MissingConfiguration(ReadSettings(action.Settings), action);
    }

    /// <inheritdoc />
    public Task<ActionResult> RunAsync(ActionRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return RunAsync(ReadSettings(context.Action.Settings), context, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ActionTestResult> TestAsync(ActionTestContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return TestAsync(ReadSettings(context.Action.Settings), context, cancellationToken);
    }

    /// <summary>
    /// The type's own check of an action's settings (<see cref="IFormBuilderActionType.CheckSettings"/>); it finds nothing
    /// unless a type overrides it.
    /// </summary>
    /// <param name="settings">The action's settings, which keep the schema and its rules.</param>
    /// <param name="check">The action and where its problems go.</param>
    protected virtual void CheckSettings(TSettings settings, ActionSettingsCheck check)
    {
    }

    /// <summary>
    /// What this site lacks for the action (<see cref="IFormBuilderActionType.MissingConfiguration"/>); nothing unless a
    /// type overrides it.
    /// </summary>
    /// <param name="settings">The action's settings.</param>
    /// <param name="action">The action.</param>
    protected virtual IReadOnlyList<string> MissingConfiguration(TSettings settings, FormAction action) => [];

    /// <summary>Runs one attempt for an entry (<see cref="IFormBuilderActionType.RunAsync"/>).</summary>
    /// <param name="settings">The action's settings.</param>
    /// <param name="context">The entry and the action.</param>
    /// <param name="cancellationToken">Cancelled when the attempt's time is up, or when the host stops.</param>
    protected abstract Task<ActionResult> RunAsync(TSettings settings, ActionRunContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Runs the action against a sample entry (<see cref="IFormBuilderActionType.TestAsync"/>); by default it reaches
    /// nobody and shows nothing, whatever the declaration says, until a type overrides it.
    /// </summary>
    /// <param name="settings">The action's settings.</param>
    /// <param name="context">The sample entry, the action and the user.</param>
    /// <param name="cancellationToken">Cancelled when the test's time is up, or when the request ends.</param>
    protected virtual Task<ActionTestResult> TestAsync(TSettings settings, ActionTestContext context, CancellationToken cancellationToken) =>
        Task.FromResult(new ActionTestResult(ActionTestOutcome.DryRun));
}
