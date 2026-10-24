using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Configuration;

/// <summary>
/// The start-up check of the runtime's globalization (ADR-0005, ADR-0042): this runtime has what Jiron Form Builder
/// needs, and a site without culture data, Unicode normalisation or IANA time zones does not start and says what to change.
/// </summary>
[TestFixture]
public sealed class GlobalizationStartupCheckTests
{
    private const string Fix =
        "Remove InvariantGlobalization from the site's project or set the environment variable DOTNET_SYSTEM_GLOBALIZATION_INVARIANT to false, "
        + "keep ICU on (System.Globalization.UseNls off), and on Linux install the ICU and tzdata packages.";

    private static readonly GlobalizationFacts Invariant = new(CultureData: false, Normalization: false, IanaTimeZones: false);

    [Test]
    public void ThisRuntime_HasCultureDataNormalisationAndIanaTimeZones()
    {
        Assert.That(GlobalizationFacts.OfThisRuntime(), Is.EqualTo(new GlobalizationFacts(CultureData: true, Normalization: true, IanaTimeZones: true)));
    }

    [Test]
    public void ARuntimeWithEveryFact_PassesTheCheck()
    {
        Assert.That(GlobalizationStartupCheck.Problem(new GlobalizationFacts(true, true, true)), Is.Null);
    }

    [TestCase(false, true, true, "culture data")]
    [TestCase(true, false, true, "Unicode normalisation")]
    [TestCase(true, true, false, "IANA time zones")]
    [TestCase(false, true, false, "culture data and IANA time zones")]
    [TestCase(false, false, false, "culture data, Unicode normalisation and IANA time zones")]
    public void AMissingFact_IsNamed_WithWhatToChange(bool cultureData, bool normalization, bool ianaTimeZones, string missing)
    {
        Assert.That(
            GlobalizationStartupCheck.Problem(new GlobalizationFacts(cultureData, normalization, ianaTimeZones)),
            Is.EqualTo($"Jiron Form Builder needs .NET globalization with ICU, and this site runs without {missing}, as invariant globalization does. {Fix}"));
    }

    [Test]
    public void ASiteWithInvariantGlobalization_DoesNotStart_AndSaysWhatToChange()
    {
        using var services = HostOptions.Services(facts: Invariant);

        Assert.That(
            () => HostOptions.Start(services),
            Throws.InstanceOf<OptionsValidationException>().With.Message.EqualTo(GlobalizationStartupCheck.Problem(Invariant)));
    }

    [Test]
    public void ASiteOnThisRuntime_Starts()
    {
        using var services = HostOptions.Services();

        Assert.That(() => HostOptions.Start(services), Throws.Nothing);
    }

    [Test]
    public void TheCheck_ReadsTheFactsItIsGiven()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new GlobalizationStartupCheck(Invariant).Validate(Options.DefaultName, new FormBuilderStartupChecks()).Failed, Is.True);
            Assert.That(new GlobalizationStartupCheck(new GlobalizationFacts(true, true, true)).Validate(null, new FormBuilderStartupChecks()).Succeeded, Is.True);
        });
    }
}
