using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Projection;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;

/// <summary>
/// The visitor texts of a visitor's form (ADR-0068, ADR-0085): its pages', groups' and fields' texts and the public text
/// settings their types mark as taking merge tags, nested ones too, in the form's order with their owners, a repeatable
/// group's children after it, and no consent's text, which the server renders whole.
/// </summary>
public sealed class VisitorTextsTests
{
    private static readonly FieldTypeRegistry Types = TestFieldTypes.Build(builder => builder.Append<CardFieldType>());

    [Test]
    public void TheTexts_ComeInTheFormsOrder_WithTheirOwnersAndProperties()
    {
        var name = Field("text", "name") with { Label = new LocalizedText("Name"), Help = new LocalizedText("As on your card"), Placeholder = new LocalizedText("Anna") };
        var guest = Field("text", "guest") with { Label = new LocalizedText("Guest") };
        var guests = Field("repeater", "guests") with { Label = new LocalizedText("Guests"), Fields = [guest] };
        var heading = Field("heading", null) with { Settings = SettingsObject.Parse("""{"text":{"":"Hello {{ field.name }}"},"level":3}""") };
        var paragraph = Field("paragraph", null) with { Settings = SettingsObject.Parse("""{"html":{"":"<p>Read me</p>"}}""") };
        var after = Field("text", "after") with { Label = new LocalizedText("After") };
        var definition = Form([name, guests, heading, paragraph, after], page => page with { Title = new LocalizedText("Page"), Description = new LocalizedHtml("<p>About</p>") }, group => group with { Title = new LocalizedText("Group") });

        var texts = VisitorTexts.Of(Shown(definition), Types);

        var page = definition.Pages[0].Key;
        var group = definition.Pages[0].Groups[0].Key;
        Assert.That(texts, Is.EqualTo(new[]
        {
            new VisitorText(page, "title", false, "Page"),
            new VisitorText(page, "description", true, "<p>About</p>"),
            new VisitorText(group, "title", false, "Group"),
            new VisitorText(name.Key, "label", false, "Name"),
            new VisitorText(name.Key, "help", false, "As on your card"),
            new VisitorText(name.Key, "placeholder", false, "Anna"),
            new VisitorText(guests.Key, "label", false, "Guests"),
            new VisitorText(guest.Key, "label", false, "Guest"),
            new VisitorText(heading.Key, "settings.text", false, "Hello {{ field.name }}"),
            new VisitorText(paragraph.Key, "settings.html", true, "<p>Read me</p>"),
            new VisitorText(after.Key, "label", false, "After"),
        }));
    }

    [Test]
    public void OnlyTheTextSettingsThatTakeMergeTags_AreTexts_NestedOnesByTheirPath_AndAConsentsTextIsNone()
    {
        var card = Field("card", "card") with
        {
            Settings = SettingsObject.Parse("""{"intro":{"":"Intro"},"note":{"":"Note"},"box":{"caption":{"":"Caption"},"size":2},"hidden":{"":"Server only"}}"""),
        };
        var consent = Field("consent", "privacy") with { Settings = SettingsObject.Parse("""{"text":{"":"I agree, {{ form.name }}."}}""") };

        var texts = VisitorTexts.Of(Shown(Form([card, consent])), Types);

        Assert.That(texts.Select(text => (text.Owner, text.Name, text.Text)), Is.EqualTo(new[]
        {
            (card.Key, "settings.intro", "Intro"),
            (card.Key, "settings.box.caption", "Caption"),
        }), "a setting without merge tags and one that is not public are none, and a consent's text is its wording");
    }

    [Test]
    public void ATextTheFormLeavesOut_IsNone()
    {
        var texts = VisitorTexts.Of(Shown(Form([Field("text", "plain")])), Types);

        Assert.That(texts, Is.Empty);
    }

    private static FormField Field(string type, string? alias) => new() { Key = Guid.NewGuid(), Type = type, Alias = alias };

    // The full form cut down to one page with one group of these fields and no texts but those the test gives.
    private static FormDefinition Form(FormField[] fields, Func<FormPage, FormPage>? page = null, Func<FormGroup, FormGroup>? group = null)
    {
        var full = DefinitionFixtures.Read(DefinitionFixtures.Full);
        var first = full.Pages[0];
        var only = first.Groups[0] with { Title = null, Description = null, Rules = [], Fields = fields };
        return full with { Pages = [(page ?? (p => p))(first with { Title = null, Description = null, Groups = [(group ?? (g => g))(only)] })] };
    }

    // The visitor's form of a definition in its default culture.
    private static PublicForm Shown(FormDefinition definition) => VisitorForm.Map(PublicProjection.Create(definition, Types), text => text.Default, Types);

    // A site's type with a text setting that takes merge tags, one that does not, a nested one that does, and one that is
    // not public.
    private sealed class CardFieldType() : FormBuilderFieldTypeBase<CardSettings>(new FieldTypeDeclaration
    {
        Alias = "card",
        Name = "Card",
        Icon = "icon-card",
        PaletteGroup = FieldPaletteGroup.Content,
    });

    private sealed class CardSettings
    {
        [FormBuilderSetting("Intro", SupportsMergeTags = true, Public = true)]
        public LocalizedText? Intro { get; init; }

        [FormBuilderSetting("Note", Public = true)]
        public LocalizedText? Note { get; init; }

        [FormBuilderSetting("Box", Public = true)]
        public CardBox Box { get; init; } = new();

        [FormBuilderSetting("Hidden", SupportsMergeTags = true)]
        public LocalizedText? Hidden { get; init; }
    }

    private sealed class CardBox
    {
        [FormBuilderSetting("Caption", SupportsMergeTags = true, Public = true)]
        public LocalizedText? Caption { get; init; }

        [FormBuilderSetting("Size", Public = true)]
        public int Size { get; init; } = 1;
    }
}
