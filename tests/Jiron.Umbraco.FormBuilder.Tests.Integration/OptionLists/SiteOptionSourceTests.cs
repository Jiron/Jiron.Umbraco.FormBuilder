using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Tests.Common.Testing;
using Umbraco.Cms.Tests.Integration.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.OptionLists;

/// <summary>
/// A real site whose composers change the option sources through <c>builder.JironFormBuilder().OptionSources()</c>
/// (ADR-0073), one before the core composer and one after it: a source added, a standard list replaced by the site's
/// own under its name and another removed. The site starts, and fields resolve what the site has.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class SiteOptionSourceTests : UmbracoIntegrationTest
{
    /// <summary>Every test's site turns the composers on and has products.</summary>
    public SiteOptionSourceTests()
    {
        InMemoryConfiguration[SiteOptionSources.Switch] = "change";
        InMemoryConfiguration[ProductsOptionSource.ProductsKey] = "tea,coffee";
    }

    [Test]
    public async Task TheSitesComposers_AddReplaceAndRemoveSources_BeforeAndAfterTheCoreComposer_AndTheSiteStarts()
    {
        var service = GetRequiredService<IFormBuilderOptionService>();
        var context = new FormBuilderOptionContext("en-GB");

        var products = await service.GetAsync(Field("products"), context);
        var months = await service.GetAsync(Field("months"), context);
        var removed = await service.GetAsync(Field("yesNoNotSure"), context);

        Assert.Multiple(() =>
        {
            Assert.That(() => GetRequiredService<IStartupValidator>().Validate(), Throws.Nothing);
            Assert.That(GetRequiredService<FormBuilderOptionSourceCollection>().Select(source => source.GetType()), Is.EqualTo(new[]
            {
                typeof(FiscalMonthsOptionSource), typeof(WeekdaysOptionSource), typeof(AgreementOptionSource), typeof(SatisfactionOptionSource), typeof(ProductsOptionSource),
            }), "the replacement in the standard list's place, the added source last");
            Assert.That(products.Options.Select(option => (option.Value, option.Label)), Is.EqualTo(new[] { ("tea", "TEA"), ("coffee", "COFFEE") }));
            Assert.That(months.Options.Select(option => option.Value), Is.EqualTo(new[] { "4", "5" }), "the site's own months under the standard name");
            Assert.That((removed.IsAvailable, removed.Options.Count), Is.EqualTo((false, 0)), "a removed source is one the site lacks");
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder) => builder.AddComposers();

    private static FormField Field(string source) => new()
    {
        Key = Guid.NewGuid(),
        Type = "dropdown",
        Alias = source,
        Options = new FieldOptions { Source = OptionSource.Code, Config = SettingsObject.Parse($$"""{"source":"{{source}}"}""") },
    };
}

/// <summary>
/// A real site whose composer adds a source under a name a standard list has (ADR-0073): the start-up validation that
/// the host runs as it starts refuses it, naming the source's type and the name. The site's own validator is wrapped
/// so that the test keeps what it threw, as the test harness starts the site before any test runs.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.None)]
public sealed class SiteOptionSourceTakenNameTests : UmbracoIntegrationTest
{
    /// <summary>The site turns on the composer that adds the taken name.</summary>
    public SiteOptionSourceTakenNameTests() => InMemoryConfiguration[SiteOptionSources.Switch] = "taken";

    [Test]
    public void ASourceWhoseNameIsTaken_StopsTheStart_NamingItsTypeAndTheName()
    {
        var validator = (RecordingStartupValidator)GetRequiredService<IStartupValidator>();

        Assert.Multiple(() =>
        {
            Assert.That(validator.Runs, Is.EqualTo(1), "the host ran it as it started");
            Assert.That(
                validator.Refusal?.Message,
                Is.EqualTo($"The option source {typeof(ShopWeekdaysOptionSource).FullName} has the name 'weekdays', which {typeof(WeekdaysOptionSource).FullName} has already."));
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        builder.AddComposers();
        var site = builder.Services.Last(service => service.ServiceType == typeof(IStartupValidator));
        var type = site.ImplementationType ?? throw new InvalidOperationException("The site's start-up validator is not registered by its type.");
        builder.Services.Remove(site);
        builder.Services.AddSingleton<IStartupValidator>(provider => new RecordingStartupValidator((IStartupValidator)ActivatorUtilities.CreateInstance(provider, type)));
    }

    // The site's start-up validator, which keeps what it refused instead of throwing it.
    private sealed class RecordingStartupValidator(IStartupValidator site) : IStartupValidator
    {
        public int Runs { get; private set; }

        public OptionsValidationException? Refusal { get; private set; }

        public void Validate()
        {
            Runs++;
            try
            {
                site.Validate();
            }
            catch (OptionsValidationException exception)
            {
                Refusal = exception;
            }
        }
    }
}
