using System.Globalization;
using System.Text;
using Jiron.Umbraco.FormBuilder.Logic;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Core.Configuration;

/// <summary>
/// What the runtime's globalization can do, as Jiron Form Builder needs it (ADR-0005, ADR-0042). Invariant
/// globalization loses all three: cultures, Unicode normalisation and, on Windows, IANA time zones.
/// </summary>
/// <param name="CultureData">Cultures have their own data: <c>de-DE</c> writes its decimals with a comma.</param>
/// <param name="Normalization">Unicode normalisation works: NFC composes <c>e</c> and U+0301 into one character.</param>
/// <param name="IanaTimeZones">IANA time zones resolve, as "today" looks them up: <c>America/New_York</c>.</param>
internal sealed record GlobalizationFacts(bool CultureData, bool Normalization, bool IanaTimeZones)
{
    /// <summary>The culture whose data the check reads.</summary>
    public const string ProbeCulture = "de-DE";

    /// <summary>The time zone the check looks up.</summary>
    public const string ProbeTimeZone = "America/New_York";

    /// <summary>The facts of the runtime this process runs on.</summary>
    public static GlobalizationFacts OfThisRuntime() =>
        new(HasCultureData(), Normalizes(), CalendarValues.FindTimeZone(ProbeTimeZone) is not null);

    private static bool HasCultureData()
    {
        try
        {
            return CultureInfo.GetCultureInfo(ProbeCulture, predefinedOnly: true).NumberFormat.NumberDecimalSeparator == ",";
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    // Invariant globalization returns the text as it was given, so the two characters stay two.
    private static bool Normalizes() =>
        string.Concat("e", ((char)0x0301).ToString()).Normalize(NormalizationForm.FormC) == ((char)0x00E9).ToString();
}

/// <summary>
/// The checks of the runtime that run when the site starts (ADR-0042). Nothing binds to it: it is registered with
/// <c>ValidateOnStart</c>, so its validators run before the site serves anything, and a failure stops it.
/// </summary>
internal sealed class FormBuilderStartupChecks;

/// <summary>Stops the site when its runtime lacks the globalization Jiron Form Builder needs (ADR-0005, ADR-0042).</summary>
/// <param name="facts">What the runtime can do.</param>
internal sealed class GlobalizationStartupCheck(GlobalizationFacts facts) : IValidateOptions<FormBuilderStartupChecks>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, FormBuilderStartupChecks options) =>
        Problem(facts) is { } problem ? ValidateOptionsResult.Fail(problem) : ValidateOptionsResult.Success;

    /// <summary>Why the runtime cannot run Jiron Form Builder, and what to change; null when it can.</summary>
    internal static string? Problem(GlobalizationFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        List<string> missing = [];
        if (!facts.CultureData)
        {
            missing.Add("culture data");
        }

        if (!facts.Normalization)
        {
            missing.Add("Unicode normalisation");
        }

        if (!facts.IanaTimeZones)
        {
            missing.Add("IANA time zones");
        }

        return missing switch
        {
            [] => null,
            _ => $"Jiron Form Builder needs .NET globalization with ICU, and this site runs without {Join(missing)}, as invariant globalization does. "
                + "Remove InvariantGlobalization from the site's project or set the environment variable DOTNET_SYSTEM_GLOBALIZATION_INVARIANT to false, "
                + "keep ICU on (System.Globalization.UseNls off), and on Linux install the ICU and tzdata packages.",
        };
    }

    private static string Join(List<string> parts) =>
        parts.Count == 1 ? parts[0] : $"{string.Join(", ", parts.Take(parts.Count - 1))} and {parts[^1]}";
}
