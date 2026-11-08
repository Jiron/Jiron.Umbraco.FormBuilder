namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>A template that ships with Jiron Form Builder (ADR-0046).</summary>
/// <param name="Key">Its fixed key, which is the form key of its envelope.</param>
/// <param name="Alias">Its alias, by which the backoffice names and describes it in its languages.</param>
/// <param name="Envelope">The export envelope it is.</param>
internal sealed record BuiltInFormTemplate(Guid Key, string Alias, string Envelope);

/// <summary>
/// The templates inside Core, as export envelopes embedded in the assembly (ADR-0002, ADR-0046): Contact from 4.5c, the
/// others from 12.3 (ADR-0217). Their texts come in English, the default text, and German.
/// </summary>
internal static class BuiltInFormTemplates
{
    /// <summary>The key of Contact: a name, an email address and a message, all required.</summary>
    public static readonly Guid ContactKey = Guid.Parse("66c2bf3e-fe43-41cc-8470-f446c2f01a89");

    /// <summary>The key of Callback: a name, a phone number, the best time of day and what it is about.</summary>
    public static readonly Guid CallbackKey = Guid.Parse("7545d6ab-fc99-4e64-b294-7afc5c356f0c");

    /// <summary>The key of Newsletter: an address verified before the entry counts, the topics and consent.</summary>
    public static readonly Guid NewsletterKey = Guid.Parse("cf4d05fe-3b37-40ff-9f4f-1ca1f599b652");

    /// <summary>The key of Event registration: sessions with places over two pages and a summary, an entry limit, a confirmation.</summary>
    public static readonly Guid EventRegistrationKey = Guid.Parse("e397fcdd-5780-48ae-ae62-69dbc586c782");

    /// <summary>The key of Job application: the person, files, consent, manual approval and a confirmation.</summary>
    public static readonly Guid JobApplicationKey = Guid.Parse("f4d2586e-e54c-437c-8e93-19d4fa364d21");

    /// <summary>The key of Feedback: a 0-to-10 scale, a grid, and a comment a low score requires.</summary>
    public static readonly Guid FeedbackKey = Guid.Parse("8c1b7750-036e-4341-a3f0-9e296b1b24bb");

    /// <summary>The key of Quote request: a service and the hours, the estimate calculated, and the contact details.</summary>
    public static readonly Guid QuoteRequestKey = Guid.Parse("56355978-d123-440f-9286-28ac4ed6344e");

    /// <summary>The key of Support request: the topic, the urgency, a description, files and the entry's reference.</summary>
    public static readonly Guid SupportRequestKey = Guid.Parse("cc3249bf-8c9b-4ed3-b4d4-3b9ca09f59f9");

    /// <summary>The key of Privacy request: a copy or the deletion of a person's data, mailed to the privacy team once verified.</summary>
    public static readonly Guid PrivacyRequestKey = Guid.Parse("0e38dd75-91c5-4e60-a9d3-6443df50df7d");

    private const string ResourcePrefix = "Jiron.Umbraco.FormBuilder.Core.Forms.Templates.";

    /// <summary>Every built-in template, in the order the gallery shows them.</summary>
    public static IReadOnlyList<BuiltInFormTemplate> All { get; } =
    [
        Load(ContactKey, "contact"),
        Load(CallbackKey, "callback"),
        Load(NewsletterKey, "newsletter"),
        Load(EventRegistrationKey, "eventRegistration"),
        Load(JobApplicationKey, "jobApplication"),
        Load(FeedbackKey, "feedback"),
        Load(QuoteRequestKey, "quoteRequest"),
        Load(SupportRequestKey, "supportRequest"),
        Load(PrivacyRequestKey, "privacyRequest"),
    ];

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
