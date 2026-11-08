using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Exchange;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>What of the library a version of a form can use by key (ADR-0214), as <c>jfbFormLibraryUse</c> names it.</summary>
internal enum LibraryUseKind
{
    /// <summary>An option list, which a field takes its options from.</summary>
    OptionList,
}

/// <summary>An item of the library that a version of a form uses.</summary>
/// <param name="Kind">What it is.</param>
/// <param name="Key">Its key.</param>
/// <param name="Count">How many of the version's elements use it: fields, for an option list.</param>
internal sealed record LibraryUse(LibraryUseKind Kind, Guid Key, int Count);

/// <summary>
/// What of the library a definition uses (ADR-0214): the option lists its fields take their options from, the children
/// of repeatable groups included, each once with how many fields use it, in key order so two reads of one
/// definition give one list.
/// </summary>
internal static class FormLibraryUses
{
    /// <summary>The items the definition uses.</summary>
    public static IReadOnlyList<LibraryUse> Of(FormDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return
        [
            .. DefinitionKeys.Fields(definition)
                .Select(field => field.Options)
                .OfType<FieldOptions>()
                .Where(options => options.Source == OptionSource.List)
                .Select(options => options.OptionList)
                .OfType<Guid>()
                .GroupBy(key => key)
                .OrderBy(group => group.Key)
                .Select(group => new LibraryUse(LibraryUseKind.OptionList, group.Key, group.Count())),
        ];
    }
}
