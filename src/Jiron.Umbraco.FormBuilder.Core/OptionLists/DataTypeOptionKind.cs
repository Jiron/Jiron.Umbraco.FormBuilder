using System.ComponentModel.DataAnnotations;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.PropertyEditors;
using Aliases = Umbraco.Cms.Core.Constants.PropertyEditors.Aliases;

namespace Jiron.Umbraco.FormBuilder.Core.OptionLists;

/// <summary>
/// The values of a data type (ADR-0075): a dropdown's, a checkbox list's or a radio button list's, in the data type's
/// order, each value its own label in every culture. The options are kept until a data type changes.
/// </summary>
/// <param name="umbraco">The data types.</param>
internal sealed class DataTypeOptionKind(OptionListUmbraco umbraco) : OptionKind<DataTypeOptionListSettings>(OptionListKind.DataType, onField: true, keeping: OptionKeeping.UntilSiteChanges)
{
    /// <inheritdoc />
    protected override async ValueTask CheckReferencesAsync(DataTypeOptionListSettings settings, OptionKindCheck check, CancellationToken cancellationToken)
    {
        // The schema requires the data type, so a configuration this runs on has one.
        if (settings.DataType is not { } key)
        {
            return;
        }

        if (await umbraco.DataTypeAsync(key) is not { } dataType)
        {
            check.Invalid("dataType", $"The site has no data type with the key {key}.");
        }
        else if (Values(dataType) is null)
        {
            check.Invalid("dataType", $"The data type '{dataType.Name}' is no dropdown, checkbox list or radio button list.");
        }
    }

    /// <inheritdoc />
    /// <remarks>A data type that is gone, or that became another kind of editor, makes the source fail.</remarks>
    protected override async ValueTask<IReadOnlyList<FormBuilderOption>> ResolveAsync(DataTypeOptionListSettings settings, OptionKindRequest request, CancellationToken cancellationToken)
    {
        var key = settings.DataType ?? throw new InvalidOperationException("The configuration names no data type.");
        var kept = OptionSiteCacheKeys.DataType(key);
        return await RuntimeCacheLoads.GetAsync(umbraco.Cache, kept, OptionSiteCacheKeys.Loading(kept), () => ReadAsync(key), OptionSiteCacheKeys.Duration, cancellationToken);
    }

    // A data type's values, or null for one that is no list of values.
    private static List<string>? Values(IDataType dataType) =>
        dataType.EditorAlias is Aliases.DropDownListFlexible or Aliases.CheckBoxList or Aliases.RadioButtonList && dataType.ConfigurationObject is ValueListConfiguration list
            ? list.Items
            : null;

    private async Task<IReadOnlyList<FormBuilderOption>> ReadAsync(Guid key)
    {
        if (await umbraco.DataTypeAsync(key) is not { } dataType || Values(dataType) is not { } values)
        {
            throw new InvalidOperationException("The data type is gone or holds no list of values.");
        }

        return [.. values.Select(value => new FormBuilderOption(value, value))];
    }
}

/// <summary>The configuration of a data type list (ADR-0075).</summary>
public sealed class DataTypeOptionListSettings
{
    /// <summary>The data type whose values are the options: a dropdown, a checkbox list or a radio button list.</summary>
    [Required]
    [FormBuilderSetting("#jfbOptionListSettings_dataType", Description = "#jfbOptionListSettings_dataTypeHelp", Editor = "dataTypePicker")]
    public Guid? DataType { get; init; }
}
