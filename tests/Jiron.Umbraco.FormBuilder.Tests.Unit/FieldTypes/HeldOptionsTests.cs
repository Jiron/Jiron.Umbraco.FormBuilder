using System.Collections.Immutable;
using System.Diagnostics;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.FieldTypes;

/// <summary>
/// The options a field's answers are held to (ADR-0077), as <see cref="FieldValidation.Options"/> gives them to every
/// type's check: the field's own or those its list or source resolved; for a field with a parent, those that belong to
/// the parent's answers; none for a field without options; and null for a source that could not give them. A site's
/// own type checks against the same set.
/// </summary>
public sealed class HeldOptionsTests
{
    private static readonly Guid Parent = new("75000000-0000-4000-8000-000000000001");
    private static readonly Guid List = new("75000000-0000-4000-8000-000000000002");

    private static readonly FieldOption[] Cities =
    [
        new() { Value = "zurich", ParentValues = ["CH"] },
        new() { Value = "berlin", ParentValues = ["DE"] },
        new() { Value = "border", ParentValues = ["CH", "DE"] },
        new() { Value = "elsewhere" },
    ];

    [Test]
    public void AFieldsOwnOptions_AreHeldAsTheyAre_WithoutAParent()
    {
        var options = new FieldOptions { Items = Cities };

        Assert.Multiple(() =>
        {
            Assert.That(Held(options), Is.EquivalentTo(new[] { "zurich", "berlin", "border", "elsewhere" }));
            Assert.That(Held(options, parentAnswers: ["CH"]), Is.EquivalentTo(new[] { "zurich", "berlin", "border", "elsewhere" }), "only a field with a parent filters, whatever its options name");
        });
    }

    [Test]
    public void ACascadingFieldsOptions_AreThoseOfTheParentsAnswers_AndThoseOfEveryAnswer()
    {
        var options = new FieldOptions { Items = Cities, Parent = Parent };

        Assert.Multiple(() =>
        {
            Assert.That(Held(options, parentAnswers: ["CH"]), Is.EquivalentTo(new[] { "zurich", "border", "elsewhere" }));
            Assert.That(Held(options, parentAnswers: ["DE"]), Is.EquivalentTo(new[] { "berlin", "border", "elsewhere" }));
            Assert.That(Held(options, parentAnswers: ["CH", "DE"]), Is.EquivalentTo(new[] { "zurich", "berlin", "border", "elsewhere" }));
            Assert.That(Held(options, parentAnswers: ["ch"]), Is.EquivalentTo(new[] { "elsewhere" }), "parent answers compare exactly");
            Assert.That(Held(options), Is.EquivalentTo(new[] { "elsewhere" }), "no parent answer, as for a hidden parent");
            Assert.That(Held(options, otherAnswers: ["CH"]), Is.EquivalentTo(new[] { "elsewhere" }), "another field's answers do not count");
        });
    }

    [Test]
    public void AListsOptions_AreThoseItResolved_FilteredByTheParentAsWell()
    {
        var resolved = new[] { new FormBuilderOption("s"), new FormBuilderOption("m") { ParentValues = ["shirt"] }, new FormBuilderOption("xl") { ParentValues = ["coat"] } };

        Assert.Multiple(() =>
        {
            Assert.That(Held(new FieldOptions { Source = OptionSource.List, OptionList = List }, resolved: resolved), Is.EquivalentTo(new[] { "s", "m", "xl" }));
            Assert.That(Held(new FieldOptions { Source = OptionSource.List, OptionList = List, Parent = Parent }, resolved: resolved, parentAnswers: ["shirt"]), Is.EquivalentTo(new[] { "s", "m" }));
            Assert.That(Held(new FieldOptions { Source = OptionSource.Code, Parent = Parent }, resolved: [], parentAnswers: ["shirt"]), Is.Empty, "a source that resolved none offers none");
        });
    }

    [Test]
    public void ASourceThatCouldNotGiveItsOptions_HoldsTheAnswersToNone_AndAFieldWithoutOptionsOffersNone()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Held(new FieldOptions { Source = OptionSource.List, OptionList = List }), Is.Null);
            Assert.That(Held(new FieldOptions { Source = OptionSource.Url, Parent = Parent }, parentAnswers: ["CH"]), Is.Null);
            Assert.That(Held(null), Is.Empty, "only a draft holds a choice without options");
            Assert.That(Held(new FieldOptions()), Is.Empty);
        });
    }

    [Test]
    public void ManyCascadingOptions_AgainstManyParentAnswers_AreHeldWithoutComparingEveryPair()
    {
        // 10,000 options that name five parent values each and one that names the last answer, against 10,000 answers of
        // one length that differ only at their end (ADR-0080): held in a list, the answers would cost 500 million long
        // comparisons, seconds; held in a set, milliseconds.
        var stem = new string('x', 50);
        var resolved = Enumerable.Range(0, 10_000)
            .Select(i => new FormBuilderOption($"o{i}") { ParentValues = [.. Enumerable.Range(0, 5).Select(j => $"{stem}{j}{i:D4}")] })
            .Append(new FormBuilderOption("last") { ParentValues = [$"{stem}a9999"] })
            .ToList();
        var answers = Enumerable.Range(0, 10_000).Select(i => $"{stem}a{i:D4}").ToArray();

        var watch = Stopwatch.StartNew();
        var held = Held(new FieldOptions { Source = OptionSource.Code, Parent = Parent }, resolved: resolved, parentAnswers: answers);
        watch.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(held, Is.EquivalentTo(new[] { "last" }));
            Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(1)), "a lookup per parent value, not a scan of the answers");
        });
    }

    [Test]
    public void ASitesOwnType_ChecksItsAnswersAgainstTheSameOptions()
    {
        var type = new TierFieldType();
        var field = new FormField { Key = Guid.NewGuid(), Type = "acme.tier", Alias = "tier", Options = new FieldOptions { Source = OptionSource.List, OptionList = List } };
        FieldMessageKinds[] Check(string answer, IReadOnlyList<FormBuilderOption>? resolved) =>
            [.. FieldValues.Check(type, field, FieldValues.Read(type, field, new FieldAnswers([answer])), new FieldCheckInput(false, ValidationCase.Today) { Options = resolved }).Errors];

        Assert.Multiple(() =>
        {
            Assert.That(Check("gold", [new FormBuilderOption("gold"), new FormBuilderOption("silver")]), Is.Empty);
            Assert.That(Check("bronze", [new FormBuilderOption("gold"), new FormBuilderOption("silver")]), Is.EqualTo(new[] { FieldMessageKinds.Invalid }));
            Assert.That(Check("bronze", null), Is.Empty, "a list that could not give its options");
        });
    }

    // The options a dropdown's check holds its answers to, with its parent's and another field's answers.
    private static IReadOnlySet<string>? Held(FieldOptions? options, IReadOnlyList<FormBuilderOption>? resolved = null, string[]? parentAnswers = null, string[]? otherAnswers = null)
    {
        var field = new FormField { Key = Guid.NewGuid(), Type = "dropdown", Alias = "choice", Options = options };
        var values = new Dictionary<Guid, ImmutableArray<string>>();
        if (parentAnswers is not null)
        {
            values[Parent] = [.. parentAnswers];
        }

        if (otherAnswers is not null)
        {
            values[Guid.NewGuid()] = [.. otherAnswers];
        }

        var input = new FieldCheckInput(false, ValidationCase.Today) { Values = values, Options = resolved };
        return new FieldValidation(field, new FieldAnswers(["zurich"]), [new FieldValue("zurich")], FieldMessageKinds.Invalid, input).Options;
    }

    // A site's choice type that holds each answer to the options it is offered.
    private sealed class TierFieldType() : FormBuilderFieldTypeBase<NoFieldSettings>(new FieldTypeDeclaration
    {
        Alias = "acme.tier",
        Name = "Tier",
        Icon = "icon-medal",
        PaletteGroup = FieldPaletteGroup.Choices,
        HoldsValue = true,
        Storage = FieldStorageKind.Text,
        RuleKind = FieldKind.Choice,
        Features = FieldFeatures.Label | FieldFeatures.Options,
        Messages = FieldMessageKinds.Invalid,
    })
    {
        protected override void Validate(NoFieldSettings settings, FieldValidation validation)
        {
            if (validation.Options is { } options && !validation.Values.All(value => options.Contains(value.Text)))
            {
                validation.Fail(FieldMessageKinds.Invalid);
            }
        }
    }
}
