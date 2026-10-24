namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>A template that ships with Jiron Form Builder (ADR-0046).</summary>
/// <param name="Key">Its fixed key, which is the form key of its envelope.</param>
/// <param name="Alias">Its alias, by which the backoffice names and describes it in its languages.</param>
/// <param name="Envelope">The export envelope it is.</param>
internal sealed record BuiltInFormTemplate(Guid Key, string Alias, string Envelope);

/// <summary>
/// The templates inside Core, as export envelopes embedded in the assembly (ADR-0002, ADR-0046): Contact from 4.5c, the
/// others from 12.3. Their texts come in English, the default text, and German.
/// </summary>
internal static class BuiltInFormTemplates
{
    /// <summary>The key of Contact: a name, an email address and a message, all required.</summary>
    public static readonly Guid ContactKey = Guid.Parse("66c2bf3e-fe43-41cc-8470-f446c2f01a89");

    private const string ResourcePrefix = "Jiron.Umbraco.FormBuilder.Core.Forms.Templates.";

    /// <summary>Every built-in template, in the order the gallery shows them.</summary>
    public static IReadOnlyList<BuiltInFormTemplate> All { get; } = [Load(ContactKey, "contact")];

    /// <summary>The built-in template with this key, or null.</summary>
    public static BuiltInFormTemplate? Find(Guid key) => All.FirstOrDefault(template => template.Key == key);

    private static BuiltInFormTemplate Load(Guid key, string alias)
    {
        var name = ResourcePrefix + alias + ".json";
        using var stream = typeof(BuiltInFormTemplates).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The built-in template {name} is missing from the assembly.");
        using var reader = new StreamReader(stream);
        return new BuiltInFormTemplate(key, alias, reader.ReadToEnd());
    }
}
