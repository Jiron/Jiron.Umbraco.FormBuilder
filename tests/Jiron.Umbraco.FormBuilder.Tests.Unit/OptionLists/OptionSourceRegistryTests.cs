using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.OptionLists;

/// <summary>
/// The site's option sources (section 4.9, ADR-0073): the collection holds the standard lists from its start, the
/// registry finds a source by its exact name, and start-up refuses a name outside the grammar or one that two sources
/// share, naming each source's type.
/// </summary>
[TestFixture]
public sealed class OptionSourceRegistryTests
{
    [Test]
    public void TheCollectionBuilder_HoldsTheStandardLists_FromItsStart()
    {
        var builder = new FormBuilderOptionSourceCollectionBuilder();
        var changed = new FormBuilderOptionSourceCollectionBuilder().Remove<SatisfactionOptionSource>().Append<ProductSource>();

        Assert.Multiple(() =>
        {
            Assert.That(builder.GetTypes(), Is.EqualTo(StandardOptionSources.All));
            Assert.That(changed.GetTypes(), Is.EqualTo(new[]
            {
                typeof(MonthsOptionSource), typeof(WeekdaysOptionSource), typeof(AgreementOptionSource), typeof(YesNoNotSureOptionSource), typeof(ProductSource),
            }), "a composer removes one and adds its own");
        });
    }

    [Test]
    public void TheRegistry_FindsASourceByItsExactName_AndLeavesOutWhatStartUpRefuses()
    {
        var products = new ProductSource();
        var registry = new OptionSourceRegistry(Collection(new MonthsOptionSource(), products, new NamedSource("Bad"), new NamedSource("products"), new NamedSource(null!)));

        Assert.Multiple(() =>
        {
            Assert.That(registry.Find("products"), Is.SameAs(products), "the first with a name keeps it");
            Assert.That(registry.Find("months"), Is.InstanceOf<MonthsOptionSource>());
            Assert.That(registry.Find("Products"), Is.Null, "names are matched exactly");
            Assert.That(registry.Find("Bad"), Is.Null);
            Assert.That(registry.Find(null!), Is.Null);
            Assert.That(registry.Ordered.Select(source => source.Name), Is.EqualTo(new[] { "months", "products" }));
        });
    }

    [Test]
    public void TheProblems_NameEachSourceWhoseNameIsTakenOrOutsideTheGrammar()
    {
        var longest = "a" + new string('B', 63);
        var problems = OptionSourceRegistry.Problems(
        [
            new MonthsOptionSource(),
            new NamedSource("months"),
            new NamedSource("Products"),
            new NamedSource("product-list"),
            new NamedSource("1st"),
            new NamedSource(string.Empty),
            new NamedSource(null!),
            new NamedSource(longest),
            new NamedSource(longest + "c"),
            new ProductSource(),
            new NamedSource("products"),
        ]);

        Assert.That(problems, Is.EqualTo(new[]
        {
            $"The option source {typeof(NamedSource).FullName} has the name 'months', which {typeof(MonthsOptionSource).FullName} has already.",
            $"The option source {typeof(NamedSource).FullName} has the name 'Products': a name is a lowercase ASCII letter, then up to 63 ASCII letters and digits.",
            $"The option source {typeof(NamedSource).FullName} has the name 'product-list': a name is a lowercase ASCII letter, then up to 63 ASCII letters and digits.",
            $"The option source {typeof(NamedSource).FullName} has the name '1st': a name is a lowercase ASCII letter, then up to 63 ASCII letters and digits.",
            $"The option source {typeof(NamedSource).FullName} has the name '': a name is a lowercase ASCII letter, then up to 63 ASCII letters and digits.",
            $"The option source {typeof(NamedSource).FullName} has the name '': a name is a lowercase ASCII letter, then up to 63 ASCII letters and digits.",
            $"The option source {typeof(NamedSource).FullName} has the name '{longest}c': a name is a lowercase ASCII letter, then up to 63 ASCII letters and digits.",
            $"The option source {typeof(NamedSource).FullName} has the name 'products', which {typeof(ProductSource).FullName} has already.",
        }));
    }

    [Test]
    public void TheStartupCheck_PassesTheStandardListsAndASitesOwnSource_AndRefusesASiteWithAProblem_NamingIt()
    {
        var valid = new OptionSourceStartupCheck(Collection([.. OptionListTestSite.Standard(), new ProductSource()]));
        var taken = new OptionSourceStartupCheck(Collection([.. OptionListTestSite.Standard(), new NamedSource("weekdays")]));

        var refused = taken.Validate(Options.DefaultName, new FormBuilderStartupChecks());

        Assert.Multiple(() =>
        {
            Assert.That(valid.Validate(Options.DefaultName, new FormBuilderStartupChecks()).Succeeded, Is.True);
            Assert.That(refused.Failed, Is.True);
            Assert.That(refused.Failures, Is.EqualTo(new[]
            {
                $"The option source {typeof(NamedSource).FullName} has the name 'weekdays', which {typeof(WeekdaysOptionSource).FullName} has already.",
            }));
        });
    }

    private static FormBuilderOptionSourceCollection Collection(params IFormBuilderOptionSource[] sources) => new(() => sources);

    /// <summary>A site's source of products, as a composer adds it.</summary>
    public sealed class ProductSource : IFormBuilderOptionSource
    {
        /// <inheritdoc />
        public string Name => "products";

        /// <inheritdoc />
        public string Label => "Products";

        /// <inheritdoc />
        public ValueTask<IReadOnlyList<FormBuilderOption>> GetOptionsAsync(FormBuilderOptionRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<FormBuilderOption>>([new FormBuilderOption("kettle", "Kettle")]);
    }

    private sealed class NamedSource(string name) : IFormBuilderOptionSource
    {
        public string Name => name;

        public string Label => name;

        public ValueTask<IReadOnlyList<FormBuilderOption>> GetOptionsAsync(FormBuilderOptionRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<FormBuilderOption>>([]);
    }
}
