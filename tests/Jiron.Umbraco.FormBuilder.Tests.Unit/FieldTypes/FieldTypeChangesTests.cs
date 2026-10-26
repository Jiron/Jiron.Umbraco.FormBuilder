using System.Collections.Immutable;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Logic;
using Jiron.Umbraco.FormBuilder.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.FieldTypes;

/// <summary>
/// The changes of type a field may make and what each loses (ADR-0059): which built-in types change, a table of built-in
/// changes restated apart from the code with everything each keeps and loses, every rule by which a setting is kept,
/// narrowed or lost, and a site's own type taking part.
/// </summary>
public sealed class FieldTypeChangesTests
{
    private const string TextOperators = "contains,endsWith,matches,notContains,startsWith";
    private const string AllTextOperators = "contains,countAtLeast,countAtMost,endsWith,equals,isEmpty,isNoneOf,isNotEmpty,isOneOf,matches,notContains,notEquals,startsWith";

    // The built-in types that never change: content holds no value, and a grid or a repeatable group keeps its values in
    // its rows or its child fields.
    private static readonly string[] Unchanging = ["grid", "repeater", "heading", "paragraph", "divider", "image", "captcha"];

    private static FieldTypeRegistry Registry => TestFieldTypes.Registry;

    // Each change as "kept | narrowed | lost | needed | properties | options | messages | operators | anew | columns"
    // ("-" for an empty list), worked out from the types' settings, common properties, error kinds and rule kinds as ADR-0059 decides.
    public static IEnumerable<TestCaseData> BuiltInChanges()
    {
        static TestCaseData Change(string from, string to, string expected) => new TestCaseData(from, to, expected).SetName($"Change({from} to {to})");

        yield return Change("text", "textarea", "minLength,maxLength,counter | - | prefix,suffix | - | none | no | none | - | no | yes");
        yield return Change("textarea", "text", "counter | minLength,maxLength | minWords,maxWords,rows,autoGrow | - | none | no | MinWords, MaxWords | - | no | yes");
        yield return Change("text", "number", $"prefix,suffix | - | minLength,maxLength,counter | - | PatternCheck | no | MinLength, MaxLength | {TextOperators} | yes | no");
        yield return Change("number", "text", "prefix,suffix | - | min,max,step,decimals | - | none | no | Invalid, Min, Max, Step, Digits | between,greaterThan,lessThan | yes | no");
        yield return Change("text", "dropdown", $"- | - | minLength,maxLength,prefix,suffix,counter | - | Placeholder, PatternCheck, MatchesFieldCheck, UniquePerFormCheck | yes | MinLength, MaxLength | {TextOperators} | yes | yes");
        yield return Change("dropdown", "radio", "- | - | multiple,minSelections,maxSelections,prompt | - | Autocomplete | no | Selections | - | no | yes");
        yield return Change("radio", "text", "- | - | other,otherLabel,layout | - | Options, Places | no | Invalid, Full | isChecked,isNotChecked | yes | yes");
        yield return Change("radio", "checkboxes", "other,otherLabel,layout | - | - | - | none | no | none | - | yes | yes");
        yield return Change("checkboxes", "ranking", "- | - | other,otherLabel,minSelections,maxSelections,layout | - | DefaultValue, Prefill, Places | no | Selections, Full | - | no | yes");
        yield return Change("date", "datetime", "defaultToday,weekdaysOnly,excluded | min,max | - | - | Autocomplete | no | none | - | yes | yes");
        yield return Change("time", "datetime", "stepMinutes | - | min,max | - | none | no | Min, Max | - | yes | no");
        yield return Change("number", "slider", "min,max,step | - | decimals,prefix,suffix | - | Placeholder, Autocomplete, MatchesFieldCheck, UniquePerFormCheck | no | Digits | - | no | yes");
        yield return Change("calculation", "number", "- | - | formula,visible,format | - | none | no | none | - | no | yes");
        yield return Change("text", "calculation", $"- | - | minLength,maxLength,prefix,suffix,counter | formula | Placeholder, Required, DefaultValue, Prefill, Autocomplete, PatternCheck, MatchesFieldCheck, UniquePerFormCheck | no | Required, MinLength, MaxLength | {TextOperators} | yes | no");
        yield return Change("scale", "rating", "lowLabel,highLabel | - | from,to | - | none | no | none | - | no | yes");
        yield return Change("checkbox", "consent", "- | text | - | - | DefaultValue, Prefill | no | none | - | no | no");
        yield return Change("consent", "checkbox", "text | - | - | - | none | no | none | - | no | no");
        yield return Change("yesno", "radio", "- | - | yesLabel,noLabel,style | - | none | yes | none | - | no | yes");
        yield return Change("country", "dropdown", "- | - | allowed,top,default | - | none | yes | none | - | no | yes");
        yield return Change("text", "name", $"- | - | minLength,maxLength,prefix,suffix,counter | - | Placeholder, DefaultValue, Autocomplete, PatternCheck, MatchesFieldCheck, UniquePerFormCheck | no | MinLength, MaxLength | {AllTextOperators} | no | no");
        yield return Change("name", "address", "- | - | parts,requiredParts | - | none | no | none | - | no | yes");
        yield return Change("file", "signature", "- | - | types,extensions,maxBytes,maxFiles,dropZone,thumbnails,camera | - | none | no | FileType, FileSize, FileCount | - | no | yes");
        yield return Change("email", "text", "- | - | confirm,confirmLabel,domains | - | none | no | Invalid, Confirmation | - | no | yes");
        yield return Change("text", "hidden", "- | - | minLength,maxLength,prefix,suffix,counter | - | HideLabel, Help, Placeholder, Required, Autocomplete, PatternCheck, MatchesFieldCheck, UniquePerFormCheck | no | Required, MinLength, MaxLength | - | no | yes");
        yield return Change("hidden", "dropdown", $"- | - | - | - | none | yes | none | {TextOperators} | yes | yes");
    }

    [Test]
    public void EveryBuiltInTypeThatHoldsAValueOfItsOwn_MayBecomeEveryOther()
    {
        var aliases = Registry.Types.Select(type => type.Declaration.Alias).ToList();
        var changing = aliases.Except(Unchanging).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(changing, Has.Count.EqualTo(26));
            foreach (var alias in aliases)
            {
                var expected = Unchanging.Contains(alias) ? [] : changing.Where(other => other != alias).ToList();
                Assert.That(FieldTypeChanges.From(Registry, alias).Select(change => change.To), Is.EqualTo(expected), $"{alias}, in the registry's order");
                Assert.That(FieldTypeChanges.From(Registry, alias).Select(change => change.From), Is.All.EqualTo(alias));
            }
        });
    }

    [TestCase(true, FieldStorageKind.Text, true)]
    [TestCase(false, FieldStorageKind.Text, false)]
    [TestCase(true, FieldStorageKind.None, false)]
    [TestCase(false, FieldStorageKind.None, false)]
    public void AType_Changes_OnlyWhenItHoldsAValueOfItsOwn(bool holdsValue, FieldStorageKind storage, bool changes)
    {
        var declaration = new FieldTypeDeclaration { Alias = "acme.other", Name = "Other", Icon = "icon-settings", PaletteGroup = FieldPaletteGroup.Text, HoldsValue = holdsValue, Storage = storage };
        var text = Registry.FindType("text")!.Declaration;

        Assert.Multiple(() =>
        {
            Assert.That(FieldTypeChanges.CanChange(text, declaration), Is.EqualTo(changes), "into it");
            Assert.That(FieldTypeChanges.CanChange(declaration, text), Is.EqualTo(changes), "from it");
        });
    }

    [TestCaseSource(nameof(BuiltInChanges))]
    public void ABuiltInChange_KeepsAndLosesWhatTheDecisionSays(string from, string to, string expected) =>
        Assert.That(Describe(Between(from, to)), Is.EqualTo(expected));

    [Test]
    public void TheBuiltInChanges_CoverEveryPartOfAChange()
    {
        var changes = BuiltInChanges().Select(data => Between((string)data.Arguments[0]!, (string)data.Arguments[1]!)).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(changes.Any(change => !change.KeptSettings.IsEmpty), Is.True, "a kept setting");
            Assert.That(changes.Any(change => !change.NarrowedSettings.IsEmpty), Is.True, "a narrowed setting");
            Assert.That(changes.Any(change => !change.LostSettings.IsEmpty), Is.True, "a lost setting");
            Assert.That(changes.Any(change => !change.NeededSettings.IsEmpty), Is.True, "a needed setting");
            Assert.That(changes.Any(change => change.LostProperties != FieldFeatures.None), Is.True, "a lost property");
            Assert.That(changes.Any(change => change.NeedsOptions), Is.True, "options needed");
            Assert.That(changes.Any(change => change.LostMessages != FieldMessageKinds.None), Is.True, "a lost message");
            Assert.That(changes.Any(change => !change.LostOperators.IsEmpty), Is.True, "a lost operator");
            Assert.That(changes.Any(change => change.ComparesAnew) && changes.Any(change => !change.ComparesAnew), Is.True, "comparing anew and not");
            Assert.That(changes.Any(change => change.KeepsColumns) && changes.Any(change => !change.KeepsColumns), Is.True, "the columns kept and not");
        });
    }

    [TestCase(FieldKind.Text, AllTextOperators)]
    [TestCase(FieldKind.Choice, "countAtLeast,countAtMost,equals,isChecked,isEmpty,isNoneOf,isNotChecked,isNotEmpty,isOneOf,notEquals")]
    [TestCase(FieldKind.Choices, "countAtLeast,countAtMost,equals,isChecked,isEmpty,isNoneOf,isNotChecked,isNotEmpty,isOneOf,notEquals")]
    [TestCase(FieldKind.Checkbox, "countAtLeast,countAtMost,equals,isChecked,isEmpty,isNoneOf,isNotChecked,isNotEmpty,isOneOf,notEquals")]
    [TestCase(FieldKind.Files, "countAtLeast,countAtMost,equals,isEmpty,isNoneOf,isNotEmpty,isOneOf,notEquals")]
    [TestCase(FieldKind.Number, "between,countAtLeast,countAtMost,equals,greaterThan,isEmpty,isNoneOf,isNotEmpty,isOneOf,lessThan,notEquals")]
    [TestCase(FieldKind.Date, "between,countAtLeast,countAtMost,equals,greaterThan,isEmpty,isNoneOf,isNotEmpty,isOneOf,lessThan,notEquals")]
    [TestCase(FieldKind.Time, "between,countAtLeast,countAtMost,equals,greaterThan,isEmpty,isNoneOf,isNotEmpty,isOneOf,lessThan,notEquals")]
    [TestCase(FieldKind.DateTime, "between,countAtLeast,countAtMost,equals,greaterThan,isEmpty,isNoneOf,isNotEmpty,isOneOf,lessThan,notEquals")]
    public void TheOperatorsOfAKind_AreThoseTheRulesTake(FieldKind kind, string operators) =>
        Assert.That(string.Join(',', Rules.OperatorsFor(kind)), Is.EqualTo(operators));

    // Each rule by which a setting of the source is kept, narrowed or lost in the target: the source's setting is
    // "s", the target's a setting of the same name, or none.
    public static IEnumerable<TestCaseData> SettingRules()
    {
        static FieldSettingDescriptor S(FieldSettingKind kind = FieldSettingKind.WholeNumber, FieldSettingRules? rules = null) =>
            new() { Name = "s", Kind = kind, Label = "S", Editor = "Umb.PropertyEditorUi.Integer", Rules = rules ?? FieldSettingRules.None };
        static FieldSettingDescriptor List(FieldSettingKind item, params string[] choices) =>
            S(FieldSettingKind.List) with { ItemKind = item, Choices = [.. choices.Select(value => new FieldSettingChoice(value, value))] };
        static FieldSettingDescriptor Choice(params string[] values) =>
            S(FieldSettingKind.Choice) with { Choices = [.. values.Select(value => new FieldSettingChoice(value, value))] };
        static FieldSettingDescriptor Nested(params FieldSettingDescriptor[] settings) => S(FieldSettingKind.Nested) with { Settings = [.. settings] };
        static FieldSettingDescriptor Inner(string name, FieldSettingKind kind = FieldSettingKind.WholeNumber, FieldSettingRules? rules = null) => S(kind, rules) with { Name = name };
        static FieldSettingRules Range(string? min, string? max, bool minExclusive = false, bool maxExclusive = false) =>
            new() { Minimum = min, Maximum = max, MinimumIsExclusive = minExclusive, MaximumIsExclusive = maxExclusive };
        static TestCaseData Case(string name, FieldSettingDescriptor from, FieldSettingDescriptor? to, string expected) =>
            new TestCaseData(from, to, expected).SetName($"Setting({name})");

        yield return Case("the same, without rules", S(), S(), "kept");
        yield return Case("missing in the target", S(), null, "lost");
        yield return Case("of another kind", S(), S(FieldSettingKind.Number), "lost");
        yield return Case("a list of another item kind", List(FieldSettingKind.Text), List(FieldSettingKind.Date), "lost");
        yield return Case("required in the target only", S(), S(rules: new() { Required = true }), "narrowed");
        yield return Case("required in both", S(rules: new() { Required = true }), S(rules: new() { Required = true }), "kept");
        yield return Case("required in the source only", S(rules: new() { Required = true }), S(), "kept");
        yield return Case("a higher lowest value", S(rules: Range("0", null)), S(rules: Range("1", null)), "narrowed");
        yield return Case("a lower lowest value", S(rules: Range("1", null)), S(rules: Range("0", null)), "kept");
        yield return Case("the same lowest value", S(rules: Range("1", null)), S(rules: Range("1", null)), "kept");
        yield return Case("the same lowest value, exclusive in the target", S(rules: Range("1", null)), S(rules: Range("1", null, minExclusive: true)), "narrowed");
        yield return Case("the same lowest value, exclusive in both", S(rules: Range("1", null, minExclusive: true)), S(rules: Range("1", null, minExclusive: true)), "kept");
        yield return Case("the same lowest value, exclusive in the source", S(rules: Range("1", null, minExclusive: true)), S(rules: Range("1", null)), "kept");
        yield return Case("a lowest value where the source has none", S(), S(rules: Range("-1000", null)), "narrowed");
        yield return Case("a higher lowest decimal", S(FieldSettingKind.Number, Range("0.25", null)), S(FieldSettingKind.Number, Range("0.5", null)), "narrowed");
        yield return Case("a lower lowest negative", S(FieldSettingKind.Number, Range("-0.5", null)), S(FieldSettingKind.Number, Range("-2", null)), "kept");
        yield return Case("a lower highest value", S(rules: Range(null, "10")), S(rules: Range(null, "5")), "narrowed");
        yield return Case("a higher highest value", S(rules: Range(null, "5")), S(rules: Range(null, "10")), "kept");
        yield return Case("the same highest value, exclusive in the target", S(rules: Range(null, "5")), S(rules: Range(null, "5", maxExclusive: true)), "narrowed");
        yield return Case("the same highest value, exclusive in both", S(rules: Range(null, "5", maxExclusive: true)), S(rules: Range(null, "5", maxExclusive: true)), "kept");
        yield return Case("a highest value where the source has none", S(), S(rules: Range(null, "1000")), "narrowed");
        yield return Case("a longer shortest text", S(FieldSettingKind.Text, new() { MinimumLength = 1 }), S(FieldSettingKind.Text, new() { MinimumLength = 2 }), "narrowed");
        yield return Case("a shortest text where the source has none", S(FieldSettingKind.Text), S(FieldSettingKind.Text, new() { MinimumLength = 1 }), "narrowed");
        yield return Case("a shorter shortest text", S(FieldSettingKind.Text, new() { MinimumLength = 2 }), S(FieldSettingKind.Text, new() { MinimumLength = 1 }), "kept");
        yield return Case("a shorter longest text", S(FieldSettingKind.Text, new() { MaximumLength = 100 }), S(FieldSettingKind.Text, new() { MaximumLength = 50 }), "narrowed");
        yield return Case("a longer longest text", S(FieldSettingKind.Text, new() { MaximumLength = 50 }), S(FieldSettingKind.Text, new() { MaximumLength = 100 }), "kept");
        yield return Case("the same longest text", S(FieldSettingKind.Text, new() { MaximumLength = 50 }), S(FieldSettingKind.Text, new() { MaximumLength = 50 }), "kept");
        yield return Case("a longest text where the source has none", S(FieldSettingKind.Text), S(FieldSettingKind.Text, new() { MaximumLength = 50 }), "narrowed");
        yield return Case("more fewest items", List(FieldSettingKind.Text) with { Rules = new() { MinimumCount = 1 } }, List(FieldSettingKind.Text) with { Rules = new() { MinimumCount = 2 } }, "narrowed");
        yield return Case("fewer fewest items", List(FieldSettingKind.Text) with { Rules = new() { MinimumCount = 2 } }, List(FieldSettingKind.Text) with { Rules = new() { MinimumCount = 1 } }, "kept");
        yield return Case("fewer most items", List(FieldSettingKind.Text) with { Rules = new() { MaximumCount = 10 } }, List(FieldSettingKind.Text) with { Rules = new() { MaximumCount = 5 } }, "narrowed");
        yield return Case("more most items", List(FieldSettingKind.Text) with { Rules = new() { MaximumCount = 5 } }, List(FieldSettingKind.Text) with { Rules = new() { MaximumCount = 10 } }, "kept");
        yield return Case("most items where the source has none", List(FieldSettingKind.Text), List(FieldSettingKind.Text) with { Rules = new() { MaximumCount = 10 } }, "narrowed");
        yield return Case("allowed values the target takes", S(FieldSettingKind.Text, new() { AllowedValues = ["a"] }), S(FieldSettingKind.Text, new() { AllowedValues = ["a", "b"] }), "kept");
        yield return Case("an allowed value the target leaves out", S(FieldSettingKind.Text, new() { AllowedValues = ["a", "c"] }), S(FieldSettingKind.Text, new() { AllowedValues = ["a", "b"] }), "narrowed");
        yield return Case("allowed values where the source takes any", S(FieldSettingKind.Text), S(FieldSettingKind.Text, new() { AllowedValues = ["a"] }), "narrowed");
        yield return Case("any value where the source allows some", S(FieldSettingKind.Text, new() { AllowedValues = ["a"] }), S(FieldSettingKind.Text), "kept");
        yield return Case("choices the target has", Choice("a", "b"), Choice("a", "b", "c"), "kept");
        yield return Case("a choice the target lacks", Choice("a", "b"), Choice("a"), "narrowed");
        yield return Case("no choice the target has", Choice("a", "b"), Choice("c"), "lost");
        yield return Case("a list of choices the target has", List(FieldSettingKind.Choice, "a"), List(FieldSettingKind.Choice, "a", "b"), "kept");
        yield return Case("a list of no choice the target has", List(FieldSettingKind.Choice, "a"), List(FieldSettingKind.Choice, "b"), "lost");
        yield return Case("an object of the same settings", Nested(Inner("x"), Inner("y")), Nested(Inner("y"), Inner("x")), "kept");
        yield return Case("an object that keeps some settings", Nested(Inner("x"), Inner("y")), Nested(Inner("x")), "narrowed");
        yield return Case("an object whose setting is narrowed", Nested(Inner("x")), Nested(Inner("x", rules: Range("1", null))), "narrowed");
        yield return Case("an object that keeps no setting", Nested(Inner("x"), Inner("y")), Nested(Inner("z")), "lost");
        yield return Case("an object whose setting has another kind", Nested(Inner("x")), Nested(Inner("x", FieldSettingKind.Text)), "lost");
        yield return Case("an object that keeps its settings but takes fewer values", Nested(Inner("x")), Nested(Inner("x")) with { Rules = new() { Required = true } }, "narrowed");
        yield return Case("any JSON", S(FieldSettingKind.Json), S(FieldSettingKind.Json), "narrowed");
        yield return Case("a list of any JSON", List(FieldSettingKind.Json), List(FieldSettingKind.Json), "narrowed");
    }

    [TestCaseSource(nameof(SettingRules))]
    public void ASetting_IsKeptNarrowedOrLost_ByItsKindAndRules(FieldSettingDescriptor from, FieldSettingDescriptor? to, string expected)
    {
        var change = FieldTypeChanges.Between(new SchemaFieldType("source", from), new SchemaFieldType("target", to is null ? [] : [to]));
        var found = new[] { ("kept", change.KeptSettings), ("narrowed", change.NarrowedSettings), ("lost", change.LostSettings) }
            .Where(part => part.Item2.Contains("s"))
            .Select(part => part.Item1)
            .ToList();

        Assert.That(found, Is.EqualTo(new[] { expected }), "in exactly one of the three");
    }

    [Test]
    public void TheTargetsRequiredSettings_ThatTheChangeDoesNotCarry_AreNeeded()
    {
        var required = new FieldSettingRules { Required = true };
        var source = new SchemaFieldType("source", Setting("carried"), Setting("bounded", FieldSettingKind.WholeNumber), Setting("lost"));
        var target = new SchemaFieldType(
            "target",
            Setting("first", rules: required),
            Setting("carried", rules: required),
            Setting("bounded", FieldSettingKind.WholeNumber, required with { Minimum = "1" }),
            Setting("lost", FieldSettingKind.Number, required),
            Setting("optional"),
            Setting("last", rules: required));

        var change = FieldTypeChanges.Between(source, target);

        Assert.Multiple(() =>
        {
            Assert.That(change.NeededSettings, Is.EqualTo(new[] { "first", "lost", "last" }), "in the target's order; a carried setting is not needed, a lost one is");
            Assert.That(change.NarrowedSettings, Is.EqualTo(new[] { "carried", "bounded" }), "required in the target only");
            Assert.That(change.LostSettings, Is.EqualTo(new[] { "lost" }));
        });
    }

    [Test]
    public void ASitesOwnType_ChangesLikeTheBuiltInOnes()
    {
        var registry = TestFieldTypes.Build(builder => builder.Append<CodeFieldType>());
        var into = FieldTypeChanges.From(registry, "text").Single(change => change.To == "acme.code");
        var back = FieldTypeChanges.From(registry, "acme.code").Single(change => change.To == "text");

        Assert.Multiple(() =>
        {
            Assert.That(Describe(into), Is.EqualTo("- | maxLength | minLength,prefix,suffix,counter | - | PatternCheck | no | MinLength | - | no | yes"));
            Assert.That(Describe(back), Is.EqualTo("maxLength | - | upper | - | none | no | none | - | no | yes"));
            Assert.That(FieldTypeChanges.From(registry, "acme.code"), Has.Length.EqualTo(26));
        });
    }

    [Test]
    public void AChangeTheFieldMayNotMake_IsRefused()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => Between("text", "text"), Throws.ArgumentException.With.Message.Contains("'text'"));
            Assert.That(() => Between("text", "heading"), Throws.ArgumentException);
            Assert.That(() => Between("grid", "radio"), Throws.ArgumentException);
            Assert.That(() => Between("radio", "repeater"), Throws.ArgumentException);
            Assert.That(FieldTypeChanges.From(Registry, "acme.missing"), Is.Empty, "a type the registry lacks");
        });
    }

    [Test]
    public void TheMap_RefusesNull()
    {
        var text = Registry.FindType("text")!;

        Assert.Multiple(() =>
        {
            Assert.That(() => FieldTypeChanges.CanChange(null!, text.Declaration), Throws.ArgumentNullException);
            Assert.That(() => FieldTypeChanges.CanChange(text.Declaration, null!), Throws.ArgumentNullException);
            Assert.That(() => FieldTypeChanges.From(null!, "text"), Throws.ArgumentNullException);
            Assert.That(() => FieldTypeChanges.From(Registry, null!), Throws.ArgumentNullException);
            Assert.That(() => FieldTypeChanges.Between(null!, text), Throws.ArgumentNullException);
            Assert.That(() => FieldTypeChanges.Between(text, null!), Throws.ArgumentNullException);
        });
    }

    private static FieldTypeChange Between(string from, string to) => FieldTypeChanges.Between(Registry.FindType(from)!, Registry.FindType(to)!);

    private static string Describe(FieldTypeChange change)
    {
        static string Names(ImmutableArray<string> names) => names.IsEmpty ? "-" : string.Join(',', names);
        static string Flags<T>(T value)
            where T : struct, Enum => Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture) == 0 ? "none" : value.ToString();
        static string YesNo(bool value) => value ? "yes" : "no";

        return string.Join(" | ", [
            Names(change.KeptSettings), Names(change.NarrowedSettings), Names(change.LostSettings), Names(change.NeededSettings),
            Flags(change.LostProperties), YesNo(change.NeedsOptions), Flags(change.LostMessages), Names(change.LostOperators),
            YesNo(change.ComparesAnew), YesNo(change.KeepsColumns),
        ]);
    }

    private static FieldSettingDescriptor Setting(string name, FieldSettingKind kind = FieldSettingKind.Text, FieldSettingRules? rules = null) =>
        new() { Name = name, Kind = kind, Label = name, Editor = "Umb.PropertyEditorUi.TextBox", Rules = rules ?? FieldSettingRules.None };

    // A type with the given settings schema and nothing else, for the rules by which settings carry over.
    private sealed class SchemaFieldType(string alias, params FieldSettingDescriptor[] settings) : IFormBuilderFieldType
    {
        public FieldTypeDeclaration Declaration { get; } = new()
        {
            Alias = alias,
            Name = alias,
            Icon = "icon-settings",
            PaletteGroup = FieldPaletteGroup.Text,
            HoldsValue = true,
            Storage = FieldStorageKind.Text,
            RuleKind = FieldKind.Text,
            Features = FieldFeatures.Label,
        };

        public FieldSettingsSchema SettingsSchema { get; } = new([.. settings]);
    }

    /// <summary>A code of letters, as a site might add one: a text without its prefixes, its minimum or its pattern.</summary>
    public sealed class CodeFieldType() : FormBuilderFieldTypeBase<CodeSettings>(new FieldTypeDeclaration
    {
        Alias = "acme.code",
        Name = "Code",
        Icon = "icon-lock",
        PaletteGroup = FieldPaletteGroup.Text,
        HoldsValue = true,
        Storage = FieldStorageKind.Text,
        RuleKind = FieldKind.Text,
        Features = FieldFeatures.Label | FieldFeatures.HideLabel | FieldFeatures.Help | FieldFeatures.Placeholder | FieldFeatures.Required
            | FieldFeatures.DefaultValue | FieldFeatures.Prefill | FieldFeatures.Autocomplete | FieldFeatures.MatchesFieldCheck
            | FieldFeatures.UniquePerFormCheck | FieldFeatures.VisibilityRules,
        Messages = FieldMessageKinds.Required | FieldMessageKinds.MaxLength,
    });

    /// <summary>The settings of a code.</summary>
    public sealed class CodeSettings
    {
        /// <summary>The most letters.</summary>
        [System.ComponentModel.DataAnnotations.Range(1, 20)]
        [FormBuilderSetting("Most letters")]
        public int MaxLength { get; init; } = 8;

        /// <summary>Whether the letters are compared in capitals.</summary>
        [FormBuilderSetting("Capitals")]
        public bool Upper { get; init; }
    }
}
