using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;

/// <summary>What the own checks of the built-in field types share (ADR-0056).</summary>
internal static class BuiltInChecks
{
    /// <summary>
    /// The ISO 3166-1 alpha-2 codes of the countries and territories, with XK for Kosovo, which browsers and .NET name
    /// too. The exceptionally reserved codes (such as EU or UK) are not countries a visitor lives in.
    /// </summary>
    public static readonly FrozenSet<string> CountryCodes = new[]
    {
        "AD", "AE", "AF", "AG", "AI", "AL", "AM", "AO", "AQ", "AR", "AS", "AT", "AU", "AW", "AX", "AZ",
        "BA", "BB", "BD", "BE", "BF", "BG", "BH", "BI", "BJ", "BL", "BM", "BN", "BO", "BQ", "BR", "BS", "BT", "BV", "BW", "BY", "BZ",
        "CA", "CC", "CD", "CF", "CG", "CH", "CI", "CK", "CL", "CM", "CN", "CO", "CR", "CU", "CV", "CW", "CX", "CY", "CZ",
        "DE", "DJ", "DK", "DM", "DO", "DZ",
        "EC", "EE", "EG", "EH", "ER", "ES", "ET",
        "FI", "FJ", "FK", "FM", "FO", "FR",
        "GA", "GB", "GD", "GE", "GF", "GG", "GH", "GI", "GL", "GM", "GN", "GP", "GQ", "GR", "GS", "GT", "GU", "GW", "GY",
        "HK", "HM", "HN", "HR", "HT", "HU",
        "ID", "IE", "IL", "IM", "IN", "IO", "IQ", "IR", "IS", "IT",
        "JE", "JM", "JO", "JP",
        "KE", "KG", "KH", "KI", "KM", "KN", "KP", "KR", "KW", "KY", "KZ",
        "LA", "LB", "LC", "LI", "LK", "LR", "LS", "LT", "LU", "LV", "LY",
        "MA", "MC", "MD", "ME", "MF", "MG", "MH", "MK", "ML", "MM", "MN", "MO", "MP", "MQ", "MR", "MS", "MT", "MU", "MV", "MW", "MX", "MY", "MZ",
        "NA", "NC", "NE", "NF", "NG", "NI", "NL", "NO", "NP", "NR", "NU", "NZ",
        "OM",
        "PA", "PE", "PF", "PG", "PH", "PK", "PL", "PM", "PN", "PR", "PS", "PT", "PW", "PY",
        "QA",
        "RE", "RO", "RS", "RU", "RW",
        "SA", "SB", "SC", "SD", "SE", "SG", "SH", "SI", "SJ", "SK", "SL", "SM", "SN", "SO", "SR", "SS", "ST", "SV", "SX", "SY", "SZ",
        "TC", "TD", "TF", "TG", "TH", "TJ", "TK", "TL", "TM", "TN", "TO", "TR", "TT", "TV", "TW", "TZ",
        "UA", "UG", "UM", "US", "UY", "UZ",
        "VA", "VC", "VE", "VG", "VI", "VN", "VU",
        "WF", "WS",
        "XK",
        "YE", "YT",
        "ZA", "ZM", "ZW",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>The place of a list's item under the settings: <c>allowed[2]</c>.</summary>
    public static string At(string setting, int index) => string.Create(CultureInfo.InvariantCulture, $"{setting}[{index}]");

    /// <summary>Refuses a lower limit above the upper one.</summary>
    public static void NotAbove(FieldSettingsCheck check, string lower, long? low, string upper, long? high)
    {
        if (low > high)
        {
            check.Invalid(lower, Invariant($"The setting '{lower}' is {low}, more than '{upper}', {high}."));
        }
    }

    /// <summary>Refuses a lower limit above the upper one, both numbers in the wire form (ADR-0005).</summary>
    public static void NotAbove(FieldSettingsCheck check, string lower, string? low, string upper, string? high)
    {
        if (low is not null && high is not null && ExactDecimal.CompareText(low, high) > 0)
        {
            check.Invalid(lower, $"The setting '{lower}' is {low}, more than '{upper}', {high}.");
        }
    }

    /// <summary>Refuses a number of choices that the field's own options cannot give (with the "Other" option, one more).</summary>
    public static void NotMoreThanOptions(FieldSettingsCheck check, string setting, int? value, int others = 0)
    {
        if (value is { } wanted && check.Field.Options is { Source: OptionSource.Items } options && wanted > options.Items.Count + others)
        {
            check.Invalid(setting, Invariant($"The setting '{setting}' is {wanted}, but the field has {FieldSettingsChecks.Count(options.Items.Count + others, "choice")}."));
        }
    }

    /// <summary>Refuses a label for the "Other" option of a field without one: it is said once, by the option itself.</summary>
    public static void OtherLabelNeedsOther(FieldSettingsCheck check, bool other, LocalizedText? label)
    {
        if (!other && label is not null)
        {
            check.Invalid("otherLabel", "The setting 'otherLabel' names the \"Other\" option, which only 'other' adds.");
        }
    }

    /// <summary>Refuses a value that is not a country code.</summary>
    public static bool IsCountry(FieldSettingsCheck check, string setting, string code)
    {
        if (CountryCodes.Contains(code))
        {
            return true;
        }

        check.Invalid(setting, $"A country is its two-letter code in capitals, such as CH, not '{code}'.");
        return false;
    }

    /// <summary>The places after the point of a number in the wire form, its trailing zeros left out.</summary>
    public static int Places(string number) => ExactDecimal.Parse(number)!.Value.S;

    /// <summary>A part of a name or an address as the settings write it.</summary>
    public static string Name<TPart>(TPart part)
        where TPart : struct, Enum => JsonNamingPolicy.CamelCase.ConvertName(part.ToString());

    /// <summary>A date's limit, a date or a date relative to today in the operand form of the rules; refuses anything else.</summary>
    public static void DateBounds(FieldSettingsCheck check, JsonElement? min, JsonElement? max)
    {
        var low = DateBound(check, "min", min);
        var high = DateBound(check, "max", max);
        if (low > high)
        {
            check.Invalid("min", "The setting 'min' is after 'max'.");
        }
    }

    // The day number of a fixed date; null for none or one relative to today, each of which keeps every order.
    private static int? DateBound(FieldSettingsCheck check, string setting, JsonElement? value)
    {
        if (value is not { } bound)
        {
            return null;
        }

        if (bound.ValueKind == JsonValueKind.String && CalendarValues.ParseDate(bound.GetString()!) is { } day)
        {
            return day;
        }

        if (bound.ValueKind == JsonValueKind.Object && bound.EnumerateObject().Count() == 1 && bound.TryGetProperty("today", out var offset)
            && offset.ValueKind == JsonValueKind.String && CalendarValues.ParseRelative(offset.GetString()!) is not null)
        {
            return null;
        }

        check.Invalid(setting, $"The setting '{setting}' is a date such as \"2026-12-31\", or a date relative to today such as {{ \"today\": \"+2d\" }}.");
        return null;
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
