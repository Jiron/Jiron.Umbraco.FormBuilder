using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Projection;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Entries;

/// <summary>
/// The wording a consent shows and its entry stores (ADR-0070): its text in the entry's culture with the usual
/// fallback, a dictionary item resolved, its merge tags rendered where consent texts render them, and its hash over the
/// culture and that text; nothing for a field that is no consent or shows no text.
/// </summary>
public sealed class ConsentWordingsTests
{
    [Test]
    public async Task AConsent_ShowsItsTextInTheEntrysCulture_HashedWithThatCulture()
    {
        var wordings = Wordings(new TestUmbraco());
        var field = Consent(new LocalizedHtml([KeyValuePair.Create(string.Empty, "I agree."), KeyValuePair.Create("de", "Ich stimme zu.")]));

        var swiss = await wordings.ShownAsync(field, MergeTagTestSite.Scope(culture: "de-CH"));
        var british = await wordings.ShownAsync(field, MergeTagTestSite.Scope(culture: "en-GB"));

        Assert.Multiple(() =>
        {
            Assert.That((swiss!.Culture, swiss.Text), Is.EqualTo(("de-CH", "Ich stimme zu.")), "German, from Swiss German");
            Assert.That(swiss.Hash, Is.EqualTo(ConsentTextHash.For("de-CH", "Ich stimme zu.")));
            Assert.That((british!.Culture, british.Text), Is.EqualTo(("en-GB", "I agree.")), "the default");
            Assert.That(british.Hash, Is.EqualTo(ConsentTextHash.For("en-GB", "I agree.")));
        });
    }

    [Test]
    public async Task ADictionaryItem_ShowsItsText_AndAMissingOneItsKey()
    {
        var umbraco = new TestUmbraco();
        umbraco.Language("en-GB", isDefault: true);
        umbraco.Item("terms", new Dictionary<string, string> { ["en-GB"] = "I accept the <a href=\"/terms\">terms</a>." });
        var wordings = Wordings(umbraco);

        var item = await wordings.ShownAsync(Consent(new LocalizedHtml("#terms")), MergeTagTestSite.Scope());
        var missing = await wordings.ShownAsync(Consent(new LocalizedHtml("#missing")), MergeTagTestSite.Scope());

        Assert.Multiple(() =>
        {
            Assert.That(item!.Text, Is.EqualTo("I accept the <a href=\"/terms\">terms</a>."));
            Assert.That(missing!.Text, Is.EqualTo("#missing"), "as merge tags show a missing item");
        });
    }

    [Test]
    public async Task MergeTags_RenderAsConsentTextsRenderThem()
    {
        var wordings = Wordings(new TestUmbraco());

        var shown = await wordings.ShownAsync(Consent(new LocalizedHtml("<p>{{ form.name }} may email me.</p> {{ field.email }}")), MergeTagTestSite.Scope());

        Assert.That(shown!.Text, Is.EqualTo("<p>Contact &lt;us&gt; may email me.</p> {{ field.email }}"), "encoded for HTML, and an answer is no part of a consent text");
    }

    [Test]
    public async Task TheWording_IsSanitisedBeforeItIsHashed_SoTheShownTheHashedAndTheStoredTextAreOne()
    {
        var umbraco = new TestUmbraco();
        umbraco.Language("en-GB", isDefault: true);
        umbraco.Item("broken", new Dictionary<string, string> { ["en-GB"] = "I agree" + (char)0xD800 + "." });
        var wordings = Wordings(umbraco);

        var hostile = await wordings.ShownAsync(Consent(new LocalizedHtml("""<p onclick="steal()">I agree<script>steal()</script> to the <a href="javascript:steal()">terms</a>.</p>""")), MergeTagTestSite.Scope());
        var broken = await wordings.ShownAsync(Consent(new LocalizedHtml("#broken")), MergeTagTestSite.Scope());
        var onlyUnsafe = await wordings.ShownAsync(Consent(new LocalizedHtml("<script>steal()</script>")), MergeTagTestSite.Scope());

        Assert.Multiple(() =>
        {
            Assert.That(hostile!.Text, Is.EqualTo("<p>I agree to the <a>terms</a>.</p>"));
            Assert.That(hostile.Hash, Is.EqualTo(ConsentTextHash.For("en-GB", hostile.Text)));
            Assert.That(broken!.Text, Is.EqualTo("I agree" + (char)0xFFFD + "."), "a lone surrogate cannot be hashed or stored, so it is replaced");
            Assert.That(onlyUnsafe, Is.Null, "nothing is left to agree to");
        });
    }

    [Test]
    public async Task AFieldThatIsNoConsent_OrShowsNoText_HasNoWording()
    {
        var wordings = Wordings(new TestUmbraco());

        Assert.Multiple(async () =>
        {
            Assert.That(await wordings.ShownAsync(new FormField { Key = Guid.NewGuid(), Type = "text", Alias = "text" }, MergeTagTestSite.Scope()), Is.Null);
            Assert.That(await wordings.ShownAsync(new FormField { Key = Guid.NewGuid(), Type = "gone", Alias = "gone" }, MergeTagTestSite.Scope()), Is.Null);
            Assert.That(await wordings.ShownAsync(new FormField { Key = Guid.NewGuid(), Type = "consent", Alias = "consent" }, MergeTagTestSite.Scope()), Is.Null, "no text");
            Assert.That(await wordings.ShownAsync(Consent(new LocalizedHtml("{{ page.name }}")), MergeTagTestSite.Scope()), Is.Null, "a text that renders as nothing");
        });
    }

    [Test]
    public void AWording_RefusesWhatCannotBeHashed()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new ConsentWording("en-GB", string.Empty), Throws.ArgumentException);
            Assert.That(() => new ConsentWording(new string('x', 33), "I agree."), Throws.ArgumentException);
            Assert.That(new ConsentWording(string.Empty, "I agree."), Has.Property(nameof(ConsentWording.Hash)).EqualTo(ConsentTextHash.For(string.Empty, "I agree.")), "the invariant culture");
        });
    }

    private static ConsentWordings Wordings(TestUmbraco umbraco) =>
        new(MergeTagTestSite.Create(umbraco: umbraco).Service, umbraco.DictionaryTexts(), new EditorHtml(), TestFieldTypes.Registry);

    private static FormField Consent(LocalizedHtml text) => new()
    {
        Key = Guid.NewGuid(),
        Type = "consent",
        Alias = "consent",
        Settings = SettingsObject.Parse(new JsonObject { ["text"] = new JsonObject(text.Texts.Select(entry => KeyValuePair.Create(entry.Culture, (JsonNode?)entry.Text))) }.ToJsonString()),
    };
}
