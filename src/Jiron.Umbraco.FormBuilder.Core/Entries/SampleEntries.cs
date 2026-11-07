using System.Collections.Immutable;
using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Core.Entries;

/// <summary>
/// The sample entry that previews and test runs show a form's texts with (ADR-0187): number 42, submitted now, in the
/// state an entry of the form arrives in, for the form as the site runs it, with the site's setting locks applied. Every
/// field that holds a value is answered with the first of a few candidates of its kind that the field itself takes: held
/// to its limits, read by its type and passed by its type's checks and its own, with the answers before it, as a member's
/// value that stands in for a hidden field is (ADR-0124). A field's own default value comes first, rendered as the page
/// renders it for the sample, and is a hidden field's only answer, as visitors send no other; a calculation is worked out
/// from the answers, and a grid takes each row's first column. The rules are not applied: a preview shows the text it is
/// asked about, not the one the rules would choose. A field that takes none of the candidates, such as a file, is not
/// answered. The preview's Fill with sample answers renders the same answers as a page posts them (ADR-0204).
/// </summary>
/// <param name="types">The field types.</param>
/// <param name="options">The options of a field with a list or another source.</param>
/// <param name="mergeTags">What renders a field's default value.</param>
/// <param name="locks">The site's setting locks, which the site runs every form with.</param>
/// <param name="zones">The site's time zone, whose today a date's checks and candidates take.</param>
/// <param name="time">The clock.</param>
internal sealed class SampleEntries(
    FieldTypeRegistry types,
    IFormBuilderOptionService options,
    IFormBuilderMergeTagService mergeTags,
    FieldSettingLocks locks,
    SiteTimeZones zones,
    TimeProvider time)
{
    /// <summary>The sample entry's number, the one the Settings view's example of a reference shows.</summary>
    public const int Number = 42;

    /// <summary>The sample person's given name.</summary>
    public const string GivenName = "Alex";

    /// <summary>The sample person's family name.</summary>
    public const string FamilyName = "Berger";

    /// <summary>The sample person's email address, at a domain kept for examples (RFC 2606).</summary>
    public const string Email = "alex.berger@example.com";

    /// <summary>The sample person's phone number, from a range kept for drama (Ofcom), so it reaches no one.</summary>
    public const string Phone = "+44 20 7946 0123";

    /// <summary>The sample web address, at a domain kept for examples (RFC 2606).</summary>
    public const string Url = "https://example.com/";

    // The candidates of a number, in the order they are tried: small first, then larger for a field that starts higher.
    private static readonly ImmutableArray<string> Numbers = ["3", "1", "0", "5", "10", "50", "100", "1000"];

    // The candidates of a time and of a date's time.
    private static readonly ImmutableArray<string> Times = ["09:30", "14:00"];

    /// <summary>
    /// The scope of the sample entry of a form's definition, in a culture: the form with the site's setting locks applied,
    /// which <see cref="MergeTagScope.Form"/> holds, the entry and its answers.
    /// </summary>
    /// <param name="definition">The definition, such as a draft the designer holds.</param>
    /// <param name="formName">The form's name.</param>
    /// <param name="formAlias">The form's alias.</param>
    /// <param name="culture">The culture its texts are in; empty for the default.</param>
    /// <param name="cancellationToken">Cancels resolving the options and rendering the default values.</param>
    public async Task<MergeTagScope> ScopeAsync(FormDefinition definition, string formName, string formAlias, string culture, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(culture);
        var (scope, today) = await BareAsync(definition, formName, formAlias, culture, cancellationToken);
        return scope with { Answers = (await SampleAsync(scope, today, cancellationToken)).Answers };
    }

    /// <summary>
    /// The sample answers of a form's definition as a page posts them (ADR-0204), by the field's key, a repeatable group's
    /// fields as one row's: what the preview renders as the starting values of its pages. Beside the answers of
    /// <see cref="ScopeAsync"/> they hold a signature's, the sample person's typed name, which the page draws and sends as
    /// the signature's image; a calculation's is the page's to work out.
    /// </summary>
    /// <param name="definition">The definition, such as the version the preview shows.</param>
    /// <param name="formName">The form's name.</param>
    /// <param name="formAlias">The form's alias.</param>
    /// <param name="culture">The culture its texts are in; empty for the default.</param>
    /// <param name="cancellationToken">Cancels resolving the options and rendering the default values.</param>
    public async Task<IReadOnlyDictionary<Guid, FieldAnswers>> PostedAsync(FormDefinition definition, string formName, string formAlias, string culture, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(culture);
        var (scope, today) = await BareAsync(definition, formName, formAlias, culture, cancellationToken);
        return (await SampleAsync(scope, today, cancellationToken)).Posted;
    }

    // The sample entry's scope without its answers, and today in the site's time zone.
    private async Task<(MergeTagScope Scope, DateOnly Today)> BareAsync(FormDefinition definition, string formName, string formAlias, string culture, CancellationToken cancellationToken)
    {
        var form = locks.Apply(definition);
        var now = time.GetUtcNow();
        var zone = await zones.GetAsync(null, cancellationToken);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var state = form.Settings.Submission.Verification is null ? SubmissionEntries.ArrivalState(form) : EntryState.Unverified;
        var scope = new MergeTagScope
        {
            Form = form,
            FormName = formName,
            FormAlias = formAlias,
            Culture = culture,
            Entry = new MergeTagEntry { Key = Guid.Empty, Number = Number, State = StoredNames.Of(state), Submitted = now },
        };
        return (scope, today);
    }

    /// <summary>
    /// The sample answers of the scope's form, in its order, a repeatable group's fields in one row; a calculation after
    /// the others, worked out from them.
    /// </summary>
    /// <param name="scope">The form, its culture, in which a text's label and a list's options are read, and the entry.</param>
    /// <param name="today">Today in the site's time zone.</param>
    /// <param name="cancellationToken">Cancels resolving the options and rendering the default values.</param>
    public async Task<MergeTagAnswers> AnswersAsync(MergeTagScope scope, DateOnly today, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return (await SampleAsync(scope, today, cancellationToken)).Answers;
    }

    // The sample answers, as the merge tags read them and as a page posts them.
    private async Task<(MergeTagAnswers Answers, Dictionary<Guid, FieldAnswers> Posted)> SampleAsync(MergeTagScope scope, DateOnly today, CancellationToken cancellationToken)
    {
        var values = new Dictionary<Guid, ImmutableArray<string>>();
        var answers = new List<MergeTagAnswer>();
        var posted = new Dictionary<Guid, FieldAnswers>();
        var fields = scope.Form.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields).SelectMany(field => field.Fields.Prepend(field)).ToList();
        foreach (var field in fields)
        {
            // A signature's answer is an image the page draws from a typed name; its merge tag reads a file, which no
            // sample has.
            if (types.Find(field.Type)?.Type is SignatureFieldType)
            {
                posted[field.Key] = new FieldAnswers([], [new KeyValuePair<string, IEnumerable<string>>(SignatureFieldType.TypedPart, [GivenName + " " + FamilyName])]);
                continue;
            }

            if (types.Find(field.Type) is not { HoldsValue: true } traits || traits.Type is CalculationFieldType || field.Fields.Count > 0)
            {
                continue;
            }

            var resolved = await OptionsAsync(field, scope.Culture, values, cancellationToken);
            var input = new FieldCheckInput(IsRequired: false, today) { Values = values, Options = resolved?.Options };
            var offered = resolved?.Values ?? Offered(field, values);
            IEnumerable<FieldAnswers> candidates = await DefaultOfAsync(field, scope with { Answers = new MergeTagAnswers(answers) }, cancellationToken) is { } given
                ? traits.ServerOnlyDefault ? [given] : Candidates(field, traits, scope.Culture, today, offered).Prepend(given)
                : traits.ServerOnlyDefault ? [] : Candidates(field, traits, scope.Culture, today, offered);
            foreach (var candidate in candidates)
            {
                if (FieldValues.StandsIn(traits.Type, field, candidate, input))
                {
                    var read = FieldValues.Read(traits.Type, field, candidate).Values;
                    values[field.Key] = [.. read.Select(value => value.Text)];
                    answers.Add(new MergeTagAnswer(field.Key, read));
                    posted[field.Key] = candidate;
                    break;
                }
            }
        }

        foreach (var field in fields)
        {
            if (types.Find(field.Type)?.Type is CalculationFieldType calculation && Calculated(field, calculation, fields, values, today) is { } value)
            {
                values[field.Key] = [value];
                answers.Add(new MergeTagAnswer(field.Key, [new FieldValue(value)]));
            }
        }

        return (new MergeTagAnswers(answers), posted);
    }

    // A field's default value as the page renders it for the sample, its tags reading the answers before it; null for a
    // field without one, or whose default renders as nothing.
    private async Task<FieldAnswers?> DefaultOfAsync(FormField field, MergeTagScope scope, CancellationToken cancellationToken)
    {
        if (field.DefaultValue is not { } template)
        {
            return null;
        }

        var texts = new List<string>();
        foreach (var text in template.Values)
        {
            var rendering = await mergeTags.RenderAsync(text, MergeTagPlace.HiddenValue, MergeTagContext.Text, scope, cancellationToken);
            if (TextSemantics.Clean(rendering.Text) is { Length: > 0 } clean)
            {
                texts.Add(clean);
            }
        }

        return texts.Count > 0 ? new FieldAnswers(texts) : null;
    }

    // A calculation's value for the sample's answers, rounded to its places as the rules round it; null where the formula
    // cannot be worked out, such as a division by zero.
    private string? Calculated(FormField field, CalculationFieldType type, IReadOnlyList<FormField> fields, Dictionary<Guid, ImmutableArray<string>> values, DateOnly today)
    {
        var settings = type.ReadSettings(field.Settings);
        if (settings.Formula is not { Length: > 0 } formula)
        {
            return null;
        }

        var byAlias = fields.Where(one => one.Alias is not null).GroupBy(one => one.Alias!, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var scope = new FormulaScope(
            alias => byAlias.TryGetValue(alias, out var named) ? types.Find(named.Type)?.KindOf(named) : null,
            alias => byAlias.TryGetValue(alias, out var named) && values.TryGetValue(named.Key, out var given) ? given : null,
            () => today.DayNumber);
        try
        {
            return Calculations.Calculate(Calculations.Parse(formula), scope) is { } result
                ? ExactDecimal.Round(result, settings.Format?.Decimals ?? CalculationFormat.DefaultDecimals).ToString()
                : null;
        }
        catch (LogicException)
        {
            return null;
        }
    }

    // The options a list or another source gives the field, for the sample answers of its parent; null for a field whose
    // options are its own, or whose source gives none.
    private async Task<(IReadOnlyList<FormBuilderOption> Options, IReadOnlyList<string> Values)?> OptionsAsync(
        FormField field, string culture, Dictionary<Guid, ImmutableArray<string>> values, CancellationToken cancellationToken)
    {
        if (field.Options is not { Source: not OptionSource.Items } source)
        {
            return null;
        }

        var parent = source.Parent is { } key && values.TryGetValue(key, out var given) ? given : [];
        var resolved = await options.GetAsync(field, new FormBuilderOptionContext(culture) { ParentValues = parent }, cancellationToken);
        return resolved.IsAvailable && resolved.Options.Count > 0 ? (resolved.Options, [.. resolved.Options.Select(option => option.Value)]) : null;
    }

    // The field's own options that its parent's sample answers leave, in their order.
    private static IReadOnlyList<string> Offered(FormField field, Dictionary<Guid, ImmutableArray<string>> values)
    {
        if (field.Options is not { } own)
        {
            return [];
        }

        var parent = own.Parent is { } key && values.TryGetValue(key, out var given) ? given : [];
        return [.. own.Items.Where(item => item.ParentValues.Count == 0 || item.ParentValues.Any(parent.Contains)).Select(item => item.Value)];
    }

    // What the field is offered, in the order it is tried: the person's for the types that ask for a person, otherwise by
    // the kind of value the type reads.
    private static IEnumerable<FieldAnswers> Candidates(FormField field, FieldTypeTraits traits, string culture, DateOnly today, IReadOnlyList<string> offered)
    {
        switch (traits.Type)
        {
            case NameFieldType:
                yield return Parts((NamePart.Title, "Dr"), (NamePart.First, GivenName), (NamePart.Middle, "J."), (NamePart.Last, FamilyName));
                yield return Parts((NamePart.First, GivenName), (NamePart.Last, FamilyName));
                yield break;
            case AddressFieldType:
                yield return Parts(
                    (AddressPart.Street, "12 Example Road"),
                    (AddressPart.Street2, "Floor 3"),
                    (AddressPart.Postcode, "12345"),
                    (AddressPart.City, "Exampleton"),
                    (AddressPart.Region, "Example County"),
                    (AddressPart.Country, RegionOf(culture) ?? "DE"));
                yield return Parts((AddressPart.Street, "12 Example Road"), (AddressPart.Postcode, "12345"), (AddressPart.City, "Exampleton"));
                yield break;
            case EmailFieldType:
                yield return One(Email);
                yield break;
            case PhoneFieldType:
                yield return One(Phone);
                yield break;
            case UrlFieldType:
                yield return One(Url);
                yield break;
            case YesNoFieldType:
                yield return One(YesNoFieldType.Yes);
                yield return One(YesNoFieldType.No);
                yield break;
            case ConsentFieldType consent:
                // Ticked, with the hash of the wording a visitor is shown, as the form posts it (ADR-0009).
                var wording = consent.ReadSettings(field.Settings).Text?.Resolve(culture) is { Length: > 0 } text ? text : LabelOf(field, culture);
                yield return new FieldAnswers(["true"], [new KeyValuePair<string, IEnumerable<string>>(ConsentFieldType.WordingPart, [ConsentTextHash.For(culture, wording)])]);
                yield break;
            case GridFieldType:
                // Each row's first column, which every row offers.
                if (offered.Count > 0 && field.Rows.Count > 0)
                {
                    yield return new FieldAnswers([], field.Rows.Select(row => new KeyValuePair<string, IEnumerable<string>>(GridFieldType.RowPart(row.Key), [offered[0]])));
                }

                yield break;
            case CountryFieldType country:
                var settings = country.ReadSettings(field.Settings);
                foreach (var code in new[] { settings.Default }.Concat(settings.Top).Concat(settings.Allowed).Append(RegionOf(culture)).Concat(["DE", "GB", "US"]).OfType<string>().Distinct(StringComparer.Ordinal))
                {
                    yield return One(code);
                }

                yield break;
        }

        switch (traits.KindOf(field))
        {
            case FieldKind.Text:
                yield return One(LabelOf(field, culture));
                break;
            case FieldKind.Choice:
                foreach (var option in offered.Take(3))
                {
                    yield return One(option);
                }

                break;
            case FieldKind.Choices:
                if (offered.Count > 0)
                {
                    yield return new FieldAnswers(offered.Take(1));
                    yield return new FieldAnswers(offered.Take(2));
                    yield return new FieldAnswers(offered);
                }

                break;
            case FieldKind.Checkbox:
                yield return One("true");
                break;
            case FieldKind.Number:
                foreach (var number in Numbers)
                {
                    yield return One(number);
                }

                break;
            case FieldKind.Date:
                foreach (var date in Dates(today))
                {
                    yield return One(date);
                }

                break;
            case FieldKind.Time:
                foreach (var at in Times)
                {
                    yield return One(at);
                }

                break;
            case FieldKind.DateTime:
                foreach (var date in Dates(today))
                {
                    yield return One(date + "T" + Times[0]);
                }

                break;
        }
    }

    // Today, a week on and thirty years back: a date the field takes now, one in the future, and a birthday.
    private static IEnumerable<string> Dates(DateOnly today) =>
        new[] { today, today.AddDays(7), today.AddYears(-30) }.Select(date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

    // The field's label in the culture as the answer of a text, or its alias where it has none or names a dictionary item.
    private static string LabelOf(FormField field, string culture)
    {
        var label = field.Label?.Resolve(culture);
        return label is { Length: > 0 } && !LocalizedString.TryGetDictionaryKey(label, out _) ? label : field.Alias ?? field.Type;
    }

    // The country of a specific culture, such as GB for en-GB; null for a neutral one.
    private static string? RegionOf(string culture)
    {
        try
        {
            return culture.Contains('-', StringComparison.Ordinal) ? new RegionInfo(culture).TwoLetterISORegionName : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static FieldAnswers One(string text) => new([text]);

    private static FieldAnswers Parts<TPart>(params (TPart Part, string Text)[] parts)
        where TPart : struct, Enum =>
        new([], parts.Select(part => new KeyValuePair<string, IEnumerable<string>>(BuiltInChecks.Name(part.Part), [part.Text])));
}
