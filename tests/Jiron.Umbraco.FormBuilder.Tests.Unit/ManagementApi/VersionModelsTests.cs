using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Comparison;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Library;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.ManagementApi;

/// <summary>The management API's models of the versions (ADR-0052): each says what its source holds, kinds by stable names.</summary>
[TestFixture]
public sealed class VersionModelsTests
{
    private static readonly DateTimeOffset Created = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Published = Created.AddHours(1);

    [Test]
    public void AVersion_SaysWhatItIs_WhoMadeIt_AndWhereItStands()
    {
        var creator = Guid.NewGuid();
        var publisher = Guid.NewGuid();

        var publish = FormVersionResponseModel.Of(new FormVersion(3, FormVersionKind.Published, 2, "Live", true, Created, creator, Published, publisher, false, true));
        var draft = FormVersionResponseModel.Of(new FormVersion(4, FormVersionKind.Draft, null, null, false, Created, null, null, null, true, false));

        Assert.Multiple(() =>
        {
            Assert.That((publish.Number, publish.Kind, publish.PublicRevision, publish.Comment, publish.IsPinned), Is.EqualTo((3, "published", (int?)2, "Live", true)));
            Assert.That((publish.CreatedUtc, publish.CreatedByUserKey, publish.PublishedUtc, publish.PublishedByUserKey), Is.EqualTo((Created, (Guid?)creator, (DateTimeOffset?)Published, (Guid?)publisher)));
            Assert.That((publish.IsDraft, publish.IsLive), Is.EqualTo((false, true)));
            Assert.That((draft.Kind, draft.PublicRevision, draft.IsDraft, draft.IsLive), Is.EqualTo(("draft", (int?)null, true, false)));
        });
    }

    [Test]
    public void AnEntry_CarriesTheSummaryOfWhatChanged_OrNoneWhenItIsNotKnown()
    {
        var version = new FormVersion(2, FormVersionKind.Draft, null, null, false, Created, null, null, null, true, false);
        var key = Guid.NewGuid();
        var parent = Guid.NewGuid();

        var entry = FormVersionEntryResponseModel.Of(new FormVersionEntry(version, [new DefinitionChangeSummary(DefinitionElementKind.ValidationRule, key, null, parent, DefinitionChangeKind.Removed, true, "Check", "check")]));
        var first = FormVersionEntryResponseModel.Of(new FormVersionEntry(version, null));

        Assert.Multiple(() =>
        {
            Assert.That(entry.Version.Number, Is.EqualTo(2));
            var change = entry.Changes!.Single();
            Assert.That((change.Kind, change.Key, change.SettingsGroup, change.ParentKey, change.Change, change.Moved, change.Label, change.Alias), Is.EqualTo(("validationRule", (Guid?)key, (string?)null, (Guid?)parent, "removed", true, "Check", "check")));
            Assert.That(first.Changes, Is.Null);
        });
    }

    [Test]
    public void AVersionsDocument_IsTheCanonicalDefinition_WithItsProblems()
    {
        var version = new FormVersion(1, FormVersionKind.Draft, null, null, false, Created, null, null, null, true, false);
        var definition = new FormDefinition { Key = Guid.NewGuid(), Pages = [new FormPage { Key = Guid.NewGuid() }] };

        var read = FormVersionDocumentResponseModel.Of(new FormVersionDocument(version, definition, [new DefinitionProblem("limit_exceeded", "$.pages", "Too many.")]));
        var unreadable = FormVersionDocumentResponseModel.Of(new FormVersionDocument(version, null, []));

        Assert.Multiple(() =>
        {
            Assert.That(read.Definition?.GetRawText(), Is.EqualTo(FormDefinitionSerializer.WriteString(definition)));
            Assert.That(read.Problems, Is.EqualTo(new[] { new DefinitionProblemResponseModel("limit_exceeded", "$.pages", "Too many.") }));
            Assert.That(read.Version.Number, Is.EqualTo(1));
            Assert.That((unreadable.Definition, unreadable.Problems.Count), Is.EqualTo(((JsonElement?)null, 0)));
        });
    }

    [Test]
    public void AComparison_CarriesEachElement_WithItsPropertiesBeforeAndAfterAsJson()
    {
        var key = Guid.NewGuid();
        var parent = Guid.NewGuid();
        var previous = Guid.NewGuid();
        var change = new DefinitionChange(
            DefinitionElementKind.Field,
            key,
            null,
            parent,
            previous,
            DefinitionChangeKind.Changed,
            true,
            "Full name",
            "name",
            "text",
            new JsonObject { ["alias"] = "name", ["required"] = false },
            new JsonObject { ["alias"] = "name", ["required"] = true },
            [new DefinitionPropertyChange(["required"], JsonValue.Create(false), JsonValue.Create(true)), new DefinitionPropertyChange(["help", "de"], null, JsonValue.Create("Hilfe"))]);
        var added = change with { ChangeKind = DefinitionChangeKind.Added, Before = null, Properties = [] };

        var model = FormComparisonResponseModel.Of(new FormComparison(2, 5, [change, added]));

        Assert.Multiple(() =>
        {
            Assert.That((model.FromVersion, model.ToVersion, model.Changes.Count), Is.EqualTo((2, 5, 2)));
            var changed = model.Changes[0];
            Assert.That((changed.Kind, changed.Key, changed.SettingsGroup, changed.ParentKey, changed.PreviousParentKey), Is.EqualTo(("field", (Guid?)key, (string?)null, (Guid?)parent, (Guid?)previous)));
            Assert.That((changed.Change, changed.Moved, changed.Label, changed.Alias, changed.Type), Is.EqualTo(("changed", true, "Full name", "name", "text")));
            Assert.That((changed.Before?.GetRawText(), changed.After?.GetRawText()), Is.EqualTo(("{\"alias\":\"name\",\"required\":false}", "{\"alias\":\"name\",\"required\":true}")));
            Assert.That(changed.Properties.Select(property => $"{string.Join("/", property.Path)}: {property.From?.GetRawText() ?? "none"} -> {property.To?.GetRawText() ?? "none"}"), Is.EqualTo(new[] { "required: false -> true", "help/de: none -> \"Hilfe\"" }));
            Assert.That((model.Changes[1].Change, model.Changes[1].Before, model.Changes[1].Properties), Is.EqualTo(("added", (JsonElement?)null, Array.Empty<DefinitionPropertyChangeResponseModel>())).Using<(string, JsonElement?, IReadOnlyList<DefinitionPropertyChangeResponseModel>)>((a, b) => a.Item1 == b.Item1 && a.Item2 is null == b.Item2 is null && a.Item3.Count == b.Item3.Count));
        });
    }

    [Test]
    public void TheNamesOfTheKinds_AreTheirModelNamesInCamelCase()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Enum.GetValues<DefinitionElementKind>().Select(ApiNames.Of), Is.EqualTo(new[] { "settings", "page", "group", "field", "rule", "validationRule", "action", "outcome" }));
            Assert.That(Enum.GetValues<DefinitionChangeKind>().Select(ApiNames.Of), Is.EqualTo(new[] { "added", "removed", "changed" }));
            Assert.That(Enum.GetValues<FormVersionKind>().Select(ApiNames.Of), Is.EqualTo(new[] { "draft", "published" }));
        });
    }
}
