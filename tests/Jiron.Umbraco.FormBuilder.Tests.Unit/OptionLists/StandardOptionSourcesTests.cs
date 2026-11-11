using Jiron.Umbraco.FormBuilder.Core.OptionLists;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.OptionLists;

/// <summary>
/// The standard lists (ADR-0073): the months and the weekdays named from culture data, the two scales and "yes, no, not
/// sure" from the language tables, each value the same in every language, in English and German with their fallbacks.
/// </summary>
[TestFixture]
public sealed class StandardOptionSourcesTests
{
    private static readonly string AUmlaut = ((char)0xE4).ToString();

    private static readonly string UUmlaut = ((char)0xFC).ToString();

    [Test]
    public async Task TheMonths_AreOneToTwelve_NamedInTheCulture_WithACapitalFirstLetter()
    {
        var english = await OptionsAsync(new MonthsOptionSource(), "en-US");
        var german = await OptionsAsync(new MonthsOptionSource(), "de-CH");
        var french = await OptionsAsync(new MonthsOptionSource(), "fr");

        Assert.Multiple(() =>
        {
            Assert.That(english.Select(option => option.Value), Is.EqualTo(Enumerable.Range(1, 12).Select(month => month.ToString(System.Globalization.CultureInfo.InvariantCulture))));
            Assert.That(english.Select(option => option.Label), Is.EqualTo(new[] { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" }));
            Assert.That(german.Select(option => option.Label), Is.EqualTo(new[] { "Januar", "Februar", "M" + AUmlaut + "rz", "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November", "Dezember" }));
            Assert.That(german.Select(option => option.Value), Is.EqualTo(english.Select(option => option.Value)), "the values are those of every language");
            Assert.That(french[0].Label, Is.EqualTo("Janvier"), "culture data writes French months in small letters");
        });
    }

    [Test]
    public async Task TheWeekdays_AreNumberedAsIso8601Does_FromTheCulturesFirstDayOfTheWeek()
    {
        var american = await OptionsAsync(new WeekdaysOptionSource(), "en-US");
        var british = await OptionsAsync(new WeekdaysOptionSource(), "en-GB");
        var german = await OptionsAsync(new WeekdaysOptionSource(), "de");
        var french = await OptionsAsync(new WeekdaysOptionSource(), "fr-FR");

        Assert.Multiple(() =>
        {
            Assert.That(american.Select(option => (option.Value, option.Label)), Is.EqualTo(new[]
            {
                ("7", "Sunday"), ("1", "Monday"), ("2", "Tuesday"), ("3", "Wednesday"), ("4", "Thursday"), ("5", "Friday"), ("6", "Saturday"),
            }));
            Assert.That(british.Select(option => option.Value), Is.EqualTo(new[] { "1", "2", "3", "4", "5", "6", "7" }), "a British week starts on Monday");
            Assert.That(german.Select(option => (option.Value, option.Label)), Is.EqualTo(new[]
            {
                ("1", "Montag"), ("2", "Dienstag"), ("3", "Mittwoch"), ("4", "Donnerstag"), ("5", "Freitag"), ("6", "Samstag"), ("7", "Sonntag"),
            }));
            Assert.That(french[0].Label, Is.EqualTo("Lundi"));
        });
    }

    [Test]
    public async Task TheScales_AndYesNoNotSure_HaveTheirWordsInSevenLanguages_AndEnglishForAnyOtherLanguage()
    {
        var agreement = await OptionsAsync(new AgreementOptionSource(), "en");
        var satisfaction = await OptionsAsync(new SatisfactionOptionSource(), "en-GB");
        var yesNo = await OptionsAsync(new YesNoNotSureOptionSource(), "en");
        var germanAgreement = await OptionsAsync(new AgreementOptionSource(), "de");
        var swissSatisfaction = await OptionsAsync(new SatisfactionOptionSource(), "de-CH");
        var germanYesNo = await OptionsAsync(new YesNoNotSureOptionSource(), "DE-at");
        var swedish = await OptionsAsync(new YesNoNotSureOptionSource(), "sv-SE");
        var added = new Dictionary<string, IReadOnlyList<FormBuilderOption>>();
        foreach (var culture in new[] { "fr-FR", "it-IT", "nl-NL", "es-ES", "da-DK" })
        {
            added[culture] = await OptionsAsync(new YesNoNotSureOptionSource(), culture);
        }

        var frenchAgreement = await OptionsAsync(new AgreementOptionSource(), "fr-CA");
        var danishSatisfaction = await OptionsAsync(new SatisfactionOptionSource(), "da");
        var none = await OptionsAsync(new AgreementOptionSource(), string.Empty);

        Assert.Multiple(() =>
        {
            Assert.That(agreement.Select(option => (option.Value, option.Label)), Is.EqualTo(new[]
            {
                ("1", "Strongly disagree"), ("2", "Disagree"), ("3", "Neither agree nor disagree"), ("4", "Agree"), ("5", "Strongly agree"),
            }));
            Assert.That(satisfaction.Select(option => (option.Value, option.Label)), Is.EqualTo(new[]
            {
                ("1", "Very dissatisfied"), ("2", "Dissatisfied"), ("3", "Neither satisfied nor dissatisfied"), ("4", "Satisfied"), ("5", "Very satisfied"),
            }));
            Assert.That(yesNo.Select(option => (option.Value, option.Label)), Is.EqualTo(new[] { ("yes", "Yes"), ("no", "No"), ("notSure", "Not sure") }));
            Assert.That(germanAgreement.Select(option => option.Label), Is.EqualTo(new[]
            {
                "Stimme " + UUmlaut + "berhaupt nicht zu", "Stimme nicht zu", "Teils, teils", "Stimme zu", "Stimme voll und ganz zu",
            }));
            Assert.That(swissSatisfaction.Select(option => option.Label), Is.EqualTo(new[]
            {
                "Sehr unzufrieden", "Unzufrieden", "Weder zufrieden noch unzufrieden", "Zufrieden", "Sehr zufrieden",
            }), "a culture takes its language's words");
            Assert.That(germanYesNo.Select(option => (option.Value, option.Label)), Is.EqualTo(new[] { ("yes", "Ja"), ("no", "Nein"), ("notSure", "Wei\u00DF nicht") }), "in any letter case");
            Assert.That(swedish.Select(option => option.Label), Is.EqualTo(new[] { "Yes", "No", "Not sure" }), "a language without words of its own takes English");
            Assert.That(added.ToDictionary(pair => pair.Key, pair => string.Join(" / ", pair.Value.Select(option => option.Label))), Is.EqualTo(new Dictionary<string, string>
            {
                ["fr-FR"] = "Oui / Non / Je ne sais pas",
                ["it-IT"] = "S\u00ec / No / Non so",
                ["nl-NL"] = "Ja / Nee / Weet ik niet",
                ["es-ES"] = "S\u00ed / No / No lo s\u00e9",
                ["da-DK"] = "Ja / Nej / Ved ikke",
            }), "the five languages 14.2 adds");
            Assert.That(frenchAgreement.Select(option => option.Label), Is.EqualTo(new[] { "Pas du tout d\u2019accord", "Pas d\u2019accord", "Ni d\u2019accord ni pas d\u2019accord", "D\u2019accord", "Tout \u00e0 fait d\u2019accord" }));
            Assert.That(danishSatisfaction.Select(option => option.Label), Is.EqualTo(new[] { "Meget utilfreds", "Utilfreds", "Hverken tilfreds eller utilfreds", "Tilfreds", "Meget tilfreds" }));
            Assert.That(none.Select(option => option.Label).First(), Is.EqualTo("Strongly disagree"), "and so does the invariant culture");
            Assert.That(StandardOptionWords.Cultures, Is.EquivalentTo(new[] { "en", "de", "fr", "it", "nl", "es", "da" }), "the tables this version has");
        });
    }

    [Test]
    public void TheStandardLists_HaveTheirNamesAndLabels_InTheOrderTheBackofficeOffersThem()
    {
        var sources = OptionListTestSite.Standard();

        Assert.Multiple(() =>
        {
            Assert.That(sources.Select(source => source.GetType()), Is.EqualTo(StandardOptionSources.All));
            Assert.That(sources.Select(source => source.Name), Is.EqualTo(new[] { "months", "weekdays", "agreement", "satisfaction", "yesNoNotSure" }));
            Assert.That(sources.Select(source => source.Label), Is.EqualTo(sources.Select(source => "#jfbOptionSources_" + source.Name)));
            Assert.That(OptionSourceRegistry.Problems(sources), Is.Empty, "their names keep the grammar");
            Assert.That(() => new MonthsOptionSource().GetOptionsAsync(null!, default), Throws.ArgumentNullException);
        });
    }

    private static async Task<IReadOnlyList<FormBuilderOption>> OptionsAsync(IFormBuilderOptionSource source, string culture) =>
        await source.GetOptionsAsync(new FormBuilderOptionRequest(culture, [], null), CancellationToken.None);
}
