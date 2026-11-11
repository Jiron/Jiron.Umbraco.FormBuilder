using Jiron.Umbraco.FormBuilder.Core.Email;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Settings;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Settings;

/// <summary>
/// A settings section's fields as its screen draws them (ADR-0218): every section reads; a field's box, order, kind,
/// editor, keys, limits, choices and default come from its record; an object nests one level deep, and a deeper one or a
/// key names its editor; a property neither a field nor host-only stops the schema.
/// </summary>
[TestFixture]
public sealed class SettingsSchemaTests
{
    [Test]
    public void EverySection_ReadsIntoTheFieldsOfItsScreen()
    {
        var sections = typeof(ISettingsSection).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && type.IsAssignableTo(typeof(ISettingsSection)))
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(sections, Has.Count.GreaterThanOrEqualTo(5), "the sections the scan found");
            foreach (var section in sections)
            {
                var alias = (string)section.GetProperty(nameof(ISettingsSection.Alias))!.GetValue(null)!;
                Assert.That(() => SettingsSchema.Read(section, alias), Throws.Nothing, section.Name);
            }
        });
    }

    [Test]
    public void General_ShowsItsBoxesInTheRecordsOrder_EachFieldAsItsTypeAndAttributeSay()
    {
        var fields = SettingsSchema.Of<GeneralSettings>();
        var byName = fields.ToDictionary(field => field.Name);
        var history = byName["history"];

        Assert.Multiple(() =>
        {
            Assert.That(fields.Select(field => (field.Box, field.Name)), Is.EqualTo(new[]
            {
                ("forms", "theme"), ("forms", "requiredMarker"), ("forms", "submitLabel"), ("forms", "nextLabel"), ("forms", "backLabel"), ("forms", "texts"),
                ("site", "baseUrl"), ("site", "timeZone"), ("newForms", "newForms"), ("history", "history"),
            }));
            Assert.That((byName["theme"].Kind, byName["theme"].Editor), Is.EqualTo(("text", "theme")));
            Assert.That((byName["timeZone"].Editor, byName["timeZone"].Default?.GetString()), Is.EqualTo(("timeZone", "UTC")));
            Assert.That(byName["requiredMarker"].Choices, Is.EqualTo(new[] { "required", "optional", "none" }), "inherit is a form's");
            Assert.That((byName["submitLabel"].Kind, byName["submitLabel"].Editor), Is.EqualTo(("localizedText", "localizedText")));
            Assert.That((byName["submitLabel"].Label, byName["submitLabel"].Description), Is.EqualTo(("#jfbGlobalSettings_general_submitLabel", "#jfbGlobalSettings_general_submitLabelHelp")));
            Assert.That((byName["texts"].Editor, byName["texts"].Label), Is.EqualTo(("visitorTexts", "#jfbGlobalSettings_general_texts")), "the visitor texts' own editor");
            Assert.That(history.Kind, Is.EqualTo("nested"));
            Assert.That(history.Fields.Select(field => (field.Name, field.Kind, field.Rules.Minimum, field.Rules.Maximum, field.Unit)), Is.EqualTo(new[]
            {
                ("draftDays", "wholeNumber", "1", "3650", "#jfbGlobalSettings_general_history_draftDaysUnit"),
                ("draftCount", "wholeNumber", "0", "1000", "#jfbGlobalSettings_general_history_draftCountUnit"),
            }));
            Assert.That(history.Fields[0].Label, Is.EqualTo("#jfbGlobalSettings_general_history_draftDays"));
            Assert.That(history.Fields[0].Default?.GetInt32(), Is.EqualTo(90));
            var newForms = byName["newForms"].Fields.ToDictionary(field => field.Name);
            Assert.That((newForms["consent"].Kind, newForms["consent"].Editor), Is.EqualTo(("json", "newFormConsent")), "an object deeper than one level names its editor");
            Assert.That(newForms["notifyCreator"].Kind, Is.EqualTo("boolean"));
        });
    }

    [Test]
    public void Files_TakesAListOfTypeChoices_AndASizeItsOwnEditorDraws()
    {
        var files = SettingsSchema.Of<FilesSettings>().ToDictionary(field => field.Name);

        Assert.Multiple(() =>
        {
            Assert.That((files["allowedTypes"].Kind, files["allowedTypes"].ItemKind, files["allowedTypes"].Choices), Is.EqualTo(("list", "choice", new[] { "images", "pdf", "office", "text", "archives" })));
            Assert.That((files["maxFileBytes"].Kind, files["maxFileBytes"].Editor, files["maxFileBytes"].Rules.Minimum), Is.EqualTo(("wholeNumber", "fileSize", "1")));
            Assert.That(files["maxFileBytes"].Default, Is.Null, "the host's limit stands while none is set");
        });
    }

    [Test]
    public void ProtectionAndEmail_TakeTheirLimits_ChoicesOfATextAndAKeysEditor_FromTheirRecords()
    {
        var protection = SettingsSchema.Of<ProtectionSettings>().ToDictionary(field => field.Name);
        var email = SettingsSchema.Of<EmailSettings>().ToDictionary(field => field.Name);

        Assert.Multiple(() =>
        {
            Assert.That(SettingsSchema.Of<ProtectionSettings>().Select(field => field.Box).Distinct(), Is.EqualTo(new[] { "level", "consent", "content", "ranges" }));
            Assert.That((protection["captchaConsent"].Kind, protection["captchaConsent"].Editor, protection["captchaConsent"].Box), Is.EqualTo(("choice", "captchaConsent", "consent")));
            Assert.That(protection["level"].Choices, Is.EqualTo(new[] { "standard", "proofOfWork", "captcha" }));
            Assert.That((protection["cookiebotCategory"].Kind, protection["cookiebotCategory"].Choices), Is.EqualTo(("choice", new[] { "necessary", "marketing" })));
            Assert.That((protection["proofBits"].Rules.Minimum, protection["proofBits"].Rules.Maximum), Is.EqualTo(("8", "24")));
            Assert.That((protection["blockedWords"].Kind, protection["blockedWords"].ItemKind, protection["blockedWords"].Rules.MaximumCount), Is.EqualTo(("list", "text", 1000)));
            Assert.That((email["defaultLayout"].Kind, email["defaultLayout"].Editor), Is.EqualTo(("key", "emailLayout")));
            Assert.That((email["senderName"].Rules.MaximumLength, email["teamRecipients"].Editor), Is.EqualTo((200, "addresses")));
        });
    }

    [Test]
    public void APropertyNeitherAFieldNorHostOnly_StopsTheSchema_NamingIt_AndAHostOnlyOneIsLeftOut()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => SettingsSchema.Read(typeof(Unmarked), "test"), Throws.InvalidOperationException.With.Message.Contains("Unmarked.Loose is no field of a settings screen"));
            Assert.That(() => SettingsSchema.Read(typeof(KeyWithoutEditor), "test"), Throws.InvalidOperationException.With.Message.Contains("has no editor of its own"));
            Assert.That(SettingsSchema.Read(typeof(WithHostOnly), "test").Select(field => field.Name), Is.EqualTo(new[] { "shown" }));
        });
    }

    private sealed record Unmarked
    {
        [SettingsField("box")]
        public bool Shown { get; init; }

        public int Loose { get; init; }
    }

    private sealed record KeyWithoutEditor
    {
        [SettingsField("box")]
        public Guid? Layout { get; init; }
    }

    private sealed record WithHostOnly
    {
        [SettingsField("box")]
        [System.ComponentModel.DataAnnotations.Range(0, 9)]
        public int Shown { get; init; }

        [HostOnly]
        public string? FromTheHost { get; init; }
    }
}
