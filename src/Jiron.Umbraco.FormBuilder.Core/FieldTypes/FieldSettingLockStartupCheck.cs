using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes;

/// <summary>
/// Stops the site when a designer lock of a field setting does not fit its field types (ADR-0060): a type or a setting
/// the site lacks, a value the setting refuses, or a required setting kept without a value. Each message names the
/// lock's key and never its value. It runs with the other start-up checks, before the site serves anything; options
/// that their own validation refuses, and field types that cannot be a registry, are left to those checks.
/// </summary>
/// <param name="host">The host configuration with the locks.</param>
/// <param name="types">The registered field types, which the check creates.</param>
internal sealed class FieldSettingLockStartupCheck(IOptions<FormBuilderOptions> host, FormBuilderFieldTypeCollection types) : IValidateOptions<FormBuilderStartupChecks>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, FormBuilderStartupChecks options)
    {
        IList<DesignerSettingLock> locks;
        FieldTypeRegistry registry;
        try
        {
            locks = host.Value.Designer.SettingLocks;
            registry = new FieldTypeRegistry(types);
        }
        catch (Exception exception) when (exception is OptionsValidationException or InvalidOperationException)
        {
            return ValidateOptionsResult.Success;
        }

        var problems = FieldSettingLocks.Problems(locks, registry);
        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }
}
