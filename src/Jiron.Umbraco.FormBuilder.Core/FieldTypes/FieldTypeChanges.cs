using System.Collections.Immutable;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes;

/// <summary>
/// The changes of type the designer offers a field, and what each loses (ADR-0059), worked out from what the types
/// declare, so a site's own types take part like the built-in ones. A field may change from one type to another when the
/// two differ, both hold a value of their own and neither stores <c>none</c>: content holds no value, and a grid or a
/// repeatable group keeps its values in rows or child fields that no other type has.
/// </summary>
internal static class FieldTypeChanges
{
    private enum SettingChange
    {
        Kept,
        Narrowed,
        Lost,
    }

    /// <summary>Whether a field of <paramref name="from"/> may become one of <paramref name="to"/>.</summary>
    public static bool CanChange(FieldTypeDeclaration from, FieldTypeDeclaration to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        return !string.Equals(from.Alias, to.Alias, StringComparison.Ordinal) && HoldsOwnValue(from) && HoldsOwnValue(to);
    }

    /// <summary>The changes a field of the type may make, in the registry's order; none for a type the registry lacks.</summary>
    public static ImmutableArray<FieldTypeChange> From(FieldTypeRegistry registry, string alias)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(alias);
        return registry.FindType(alias) is { } from
            ? [.. registry.Types.Where(to => CanChange(from.Declaration, to.Declaration)).Select(to => Between(from, to))]
            : [];
    }

    /// <summary>What changing a field of <paramref name="from"/> into one of <paramref name="to"/> keeps and loses.</summary>
    /// <exception cref="ArgumentException">The field may not make that change (<see cref="CanChange"/>).</exception>
    public static FieldTypeChange Between(IFormBuilderFieldType from, IFormBuilderFieldType to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        var source = from.Declaration;
        var target = to.Declaration;
        if (!CanChange(source, target))
        {
            throw new ArgumentException($"A field of '{source.Alias}' cannot become one of '{target.Alias}': a change takes two different types that both hold a value of their own.", nameof(to));
        }

        var settings = from.SettingsSchema.Settings.Select(setting => (setting.Name, Change: Compare(setting, to.SettingsSchema.Find(setting.Name)))).ToList();
        var carried = settings.Where(each => each.Change != SettingChange.Lost).Select(each => each.Name).ToHashSet(StringComparer.Ordinal);
        return new FieldTypeChange
        {
            From = source.Alias,
            To = target.Alias,
            KeptSettings = Named(settings, SettingChange.Kept),
            NarrowedSettings = Named(settings, SettingChange.Narrowed),
            LostSettings = Named(settings, SettingChange.Lost),
            NeededSettings = [.. to.SettingsSchema.Settings.Where(setting => setting.Rules.Required && !carried.Contains(setting.Name)).Select(setting => setting.Name)],
            LostProperties = source.Features & ~target.Features & ~FieldFeatures.NeedsOptions,
            NeedsOptions = target.Features.HasFlag(FieldFeatures.NeedsOptions) && !source.Features.HasFlag(FieldFeatures.Options),
            LostMessages = source.Messages & ~target.Messages,
            LostOperators = LostOperators(source.RuleKind, target.RuleKind),
            ComparesAnew = source.RuleKind is { } a && target.RuleKind is { } b && a != b,
            KeepsColumns = Columns(source.Storage) == Columns(target.Storage),
        };
    }

    private static bool HoldsOwnValue(FieldTypeDeclaration declaration) => declaration.HoldsValue && declaration.Storage != FieldStorageKind.None;

    private static ImmutableArray<string> Named(List<(string Name, SettingChange Change)> settings, SettingChange change) =>
        [.. settings.Where(each => each.Change == change).Select(each => each.Name)];

    private static ImmutableArray<string> LostOperators(FieldKind? from, FieldKind? to) => (from, to) switch
    {
        (null, _) => [],
        ({ } source, null) => Rules.OperatorsFor(source),
        ({ } source, { } target) => [.. Rules.OperatorsFor(source).Except(Rules.OperatorsFor(target), StringComparer.Ordinal)],
    };

    // Long text has text's columns: the text and its key (ADR-0058).
    private static FieldStorageKind Columns(FieldStorageKind storage) => storage == FieldStorageKind.LongText ? FieldStorageKind.Text : storage;

    private static SettingChange Compare(FieldSettingDescriptor from, FieldSettingDescriptor? to)
    {
        if (to is null || from.Kind != to.Kind || from.ItemKind != to.ItemKind)
        {
            return SettingChange.Lost;
        }

        var change = SettingChange.Kept;
        if (!from.Settings.IsEmpty)
        {
            var nested = from.Settings.Select(setting => Compare(setting, to.Settings.FirstOrDefault(each => string.Equals(each.Name, setting.Name, StringComparison.Ordinal)))).ToList();
            if (nested.All(each => each == SettingChange.Lost))
            {
                return SettingChange.Lost;
            }

            change = nested.All(each => each == SettingChange.Kept) ? change : SettingChange.Narrowed;
        }

        if (!from.Choices.IsEmpty)
        {
            var values = to.Choices.Select(choice => choice.Value).ToHashSet(StringComparer.Ordinal);
            var kept = from.Choices.Count(choice => values.Contains(choice.Value));
            if (kept == 0)
            {
                return SettingChange.Lost;
            }

            change = kept == from.Choices.Length ? change : SettingChange.Narrowed;
        }

        // Any JSON is the target's to check (a date's bound, say), so nothing says it takes the source's values.
        var checkedByType = from.Kind == FieldSettingKind.Json || from.ItemKind == FieldSettingKind.Json;
        return checkedByType || !Takes(to.Rules, from.Rules) ? SettingChange.Narrowed : change;
    }

    // Whether every value the source's rules take, the target's take too.
    private static bool Takes(FieldSettingRules to, FieldSettingRules from) =>
        (!to.Required || from.Required)
        && Bound(to.Minimum, to.MinimumIsExclusive, from.Minimum, from.MinimumIsExclusive, below: true)
        && Bound(to.Maximum, to.MaximumIsExclusive, from.Maximum, from.MaximumIsExclusive, below: false)
        && (to.MinimumLength ?? 0) <= (from.MinimumLength ?? 0)
        && Most(to.MaximumLength, from.MaximumLength)
        && (to.MinimumCount ?? 0) <= (from.MinimumCount ?? 0)
        && Most(to.MaximumCount, from.MaximumCount)
        && (to.AllowedValues.IsEmpty || (!from.AllowedValues.IsEmpty && from.AllowedValues.All(value => to.AllowedValues.Contains(value, StringComparer.Ordinal))));

    // Whether the target's lowest (or highest) value takes the source's values: it has none, or it lies at or beyond the
    // source's, where an exclusive bound takes less than the same inclusive one.
    private static bool Bound(string? to, bool toExclusive, string? from, bool fromExclusive, bool below)
    {
        if (to is null)
        {
            return true;
        }

        if (from is null || ExactDecimal.CompareText(to, from) is not { } order)
        {
            return false;
        }

        return (below ? order < 0 : order > 0) || (order == 0 && (!toExclusive || fromExclusive));
    }

    // Whether the target's most characters or items take the source's: it has no most, or at least the source's.
    private static bool Most(int? to, int? from) => to is null || (from is not null && to >= from);
}
