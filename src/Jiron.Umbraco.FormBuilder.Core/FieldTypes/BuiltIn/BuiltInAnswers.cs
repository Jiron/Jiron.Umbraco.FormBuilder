namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;

/// <summary>How the built-in types with options or parts read their answers (ADR-0058).</summary>
internal static class BuiltInAnswers
{
    /// <summary>The part that holds the visitor's own text when a field offers "Other".</summary>
    public const string OtherPart = "other";

    /// <summary>
    /// The chosen options' values, then the "Other" text when the field offers it: after the options when several may
    /// be chosen, and in place of an option when one may. Two options where one may be chosen, two "Other" texts or a
    /// value given twice are unreadable; an "Other" text the field does not offer is ignored, and so is one that repeats
    /// a chosen option.
    /// </summary>
    public static void ReadChoices(FieldReading reading, bool several, bool other)
    {
        var chosen = reading.Answers.Values;
        var others = other ? reading.Answers.Part(OtherPart) : [];
        if ((!several && chosen.Length > 1) || others.Length > 1 || chosen.Distinct(StringComparer.Ordinal).Count() != chosen.Length)
        {
            reading.Invalid();
            return;
        }

        foreach (var value in chosen)
        {
            reading.Add(value);
        }

        if (others is [var text] && (several || chosen.IsEmpty) && !chosen.Contains(text, StringComparer.Ordinal))
        {
            reading.Add(new FieldValue(text) { IsOwnText = true });
        }
    }

    /// <summary>
    /// One value of the parts the field asks for, in the given order, as a JSON object (<see cref="FieldPartsJson"/>):
    /// parts left empty are left out, and parts the field does not ask for are ignored; a part given twice is unreadable.
    /// </summary>
    /// <param name="reading">The field, its answers, and where the value goes.</param>
    /// <param name="asked">The names of the parts the field asks for, in the type's order.</param>
    public static void ReadParts(FieldReading reading, IEnumerable<string> asked)
    {
        var parts = new List<KeyValuePair<string, string>>();
        foreach (var name in asked)
        {
            switch (reading.Answers.Part(name))
            {
                case []:
                    break;
                case [var text]:
                    parts.Add(new(name, text));
                    break;
                default:
                    reading.Invalid();
                    return;
            }
        }

        if (FieldPartsJson.Write(parts) is { } value)
        {
            reading.Add(value);
        }
    }
}
