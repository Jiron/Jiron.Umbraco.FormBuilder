using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Logic;
using Jiron.Umbraco.FormBuilder.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Rendering;

/// <summary>
/// The culture data the rendering fixtures are rendered with (ADR-0263), from <c>tests/fixtures/rendering/culture-data.json</c>:
/// the formats of <c>en-US</c> and <c>de-DE</c>, and the countries' names in English and German in their order. The
/// operating system's ICU writes these differently on Windows and on Linux, and again after its updates, so the fixtures,
/// which the client's tests read as the server's rendering, render with this data instead and are the same file on
/// every system. Everything else of the rendering is the product's own, as on a site.
/// </summary>
internal static class FixedCultureData
{
    /// <summary>Makes the host render with the fixed culture data in place of the system's.</summary>
    public static IUmbracoBuilder RendersWithFixedCultureData(this IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddUnique(Read());
        return builder;
    }

    private static MergeTagCultures Read()
    {
        var file = RepositoryRoot.Path(typeof(FixedCultureData).Assembly, "tests", "fixtures", "rendering", "culture-data.json");
        using var document = JsonDocument.Parse(File.ReadAllText(file));
        var root = document.RootElement;
        var formats = root.GetProperty("formats").EnumerateObject().Select(culture => KeyValuePair.Create(culture.Name, Formats(culture.Value))).ToList();
        var countries = new List<KeyValuePair<string, IReadOnlyList<KeyValuePair<string, string>>>>();
        foreach (var language in root.GetProperty("countries").EnumerateObject())
        {
            List<KeyValuePair<string, string>> list = [.. language.Value.EnumerateArray().Select(row => KeyValuePair.Create(row[0].GetString()!, row[1].GetString()!))];
            var missing = BuiltInChecks.CountryCodes.Except(list.Select(pair => pair.Key), StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            if (missing.Count > 0)
            {
                throw new InvalidOperationException($"{file} names no country {string.Join(", ", missing)} in '{language.Name}'; add each with its name in its place.");
            }

            countries.Add(KeyValuePair.Create<string, IReadOnlyList<KeyValuePair<string, string>>>(language.Name, list));
        }

        return new MergeTagCultures(formats, countries);
    }

    // A culture's formats as MergeTagCultures.Write writes them, the shape the browser's runtime reads.
    private static CultureFormats Formats(JsonElement element)
    {
        var dates = element.GetProperty("dates");
        var number = element.GetProperty("number");
        var currency = element.GetProperty("currency");
        return new CultureFormats(
            new DatePatterns(Text(dates, "short"), Text(dates, "medium"), Text(dates, "long"), Text(dates, "time"), Text(dates, "dateTime")),
            Strings(element, "months"),
            Strings(element, "monthsShort"),
            Strings(element, "monthsGenitive"),
            Strings(element, "monthsShortGenitive"),
            Strings(element, "days"),
            Strings(element, "daysShort"),
            Text(element, "am"),
            Text(element, "pm"),
            Text(element, "minus"),
            new NumberFormat(Text(number, "decimal"), Text(number, "group"), Sizes(number), Text(number, "negative")),
            new CurrencyFormat(Text(currency, "code"), Text(currency, "symbol"), Text(currency, "decimal"), Text(currency, "group"), Sizes(currency), Text(currency, "positive"), Text(currency, "negative")));

        static string Text(JsonElement parent, string name) => parent.GetProperty(name).GetString()!;
        static List<string> Strings(JsonElement parent, string name) => [.. parent.GetProperty(name).EnumerateArray().Select(each => each.GetString()!)];
        static List<int> Sizes(JsonElement parent) => [.. parent.GetProperty("sizes").EnumerateArray().Select(size => size.GetInt32())];
    }
}
