using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// The templates users save, through the management API (ROADMAP 12.1, ADR-0217): a form's draft saved as a template that
/// the gallery lists after the built-in ones, renamed and deleted, each change audited; a group saved as the canvas holds
/// it, every condition that names an element outside it left out and counted; and who may do which.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class TemplateApiTests : ManagementApiTestBase
{
    private const string Designer = "designer";

    [Test]
    public async Task AFormsDraft_IsSavedAsATemplate_ListedAfterTheBuiltInOnes_RenamedAndDeleted_EachAudited()
    {
        var form = await FormAsync("Enquiry");
        await FillAsync(form);

        var saved = await CallAsync(Designer, HttpMethod.Post, $"template/form/{form}", new { name = "  Enquiry template ", description = "For enquiries" });
        Assert.That(saved.Outcome, Is.EqualTo("201"), saved.ToString());
        var key = saved.Body!["key"]!.GetValue<Guid>();
        var gallery = await CallAsync(Designer, HttpMethod.Get, "template");
        var renamed = await CallAsync(Designer, HttpMethod.Put, $"template/{key}", new { name = "Enquiries" });
        var deleted = await CallAsync(Designer, HttpMethod.Delete, $"template/{key}");
        var after = await CallAsync(Designer, HttpMethod.Get, "template");
        var audited = (await GetRequiredService<IAuditRepository>().QueryAsync(new AuditQuery { EntityKey = key }, PageRequest.First(10))).Items;

        Assert.Multiple(async () =>
        {
            Assert.That((saved.Body!["name"]!.GetValue<string>(), saved.Body!["description"]!.GetValue<string>(), saved.Body!["kind"]!.GetValue<string>(), saved.Body!["builtIn"]!.GetValue<bool>()), Is.EqualTo(("Enquiry template", "For enquiries", "form", false)));
            Assert.That(saved.Body!["holds"]!["fields"]!.GetValue<int>(), Is.EqualTo(1), "the draft's field");
            var items = gallery.Body!["items"]!.AsArray();
            Assert.That((gallery.Body!["total"]!.GetValue<int>(), items[^1]!["key"]!.GetValue<Guid>()), Is.EqualTo((BuiltInFormTemplates.All.Count + 1, key)), "after the built-in ones");
            Assert.That(items.Take(BuiltInFormTemplates.All.Count).Select(item => item!["alias"]!.GetValue<string>()), Is.EqualTo(BuiltInFormTemplates.All.Select(template => template.Alias)));
            Assert.That((renamed.Outcome, renamed.Body!["name"]!.GetValue<string>(), renamed.Body!["description"]), Is.EqualTo(("200", "Enquiries", (JsonNode?)null)), "a rename without a description clears it");
            Assert.That(deleted.Outcome, Is.EqualTo("204"));
            Assert.That(after.Body!["total"]!.GetValue<int>(), Is.EqualTo(BuiltInFormTemplates.All.Count));
            Assert.That((await CallAsync(Designer, HttpMethod.Delete, $"template/{key}")).Outcome, Is.EqualTo("404 template_not_found"));
            Assert.That((await CallAsync(Designer, HttpMethod.Delete, $"template/{BuiltInFormTemplates.ContactKey}")).Outcome, Is.EqualTo("404 template_not_found"), "a built-in template stays");
            Assert.That((await CallAsync(Designer, HttpMethod.Put, $"template/{BuiltInFormTemplates.ContactKey}", new { name = "Mine" })).Outcome, Is.EqualTo("404 template_not_found"));
            Assert.That(audited.Select(entry => entry.Action).Order(), Is.EqualTo(new[] { FormTemplateLibrary.DeletedAction, FormTemplateLibrary.RenamedAction, FormTemplateLibrary.SavedAction }));
            Assert.That(audited.Single(entry => entry.Action == FormTemplateLibrary.SavedAction).Details, Is.EqualTo($$"""{"kind":"form","form":"{{form}}"}"""));
        });
    }

    [Test]
    public async Task AGroup_IsSavedAsTheCanvasHoldsIt_WhatPointsOutsideItLeftOut()
    {
        var outside = Guid.NewGuid();
        var wants = Guid.NewGuid();
        var group = new
        {
            key = Guid.NewGuid(),
            title = new Dictionary<string, string> { [""] = "Address" },
            rules = new[] { Rule(outside) },
            fields = new object[]
            {
                new { key = wants, type = "yesno", alias = "byPost", label = Text("By post?") },
                new { key = Guid.NewGuid(), type = "text", alias = "street", label = Text("Street"), rules = new[] { Rule(wants) } },
                new { key = Guid.NewGuid(), type = "text", alias = "note", label = Text("Note"), rules = new[] { Rule(outside) } },
                new
                {
                    key = Guid.NewGuid(),
                    type = "dropdown",
                    alias = "region",
                    label = Text("Region"),
                    options = new { parent = outside, items = new[] { new { value = "north", label = Text("North"), parentValues = new[] { "yes" } } } },
                },
            },
        };

        var saved = await CallAsync(Designer, HttpMethod.Post, "template/group", new { name = "Address block", group });
        var listed = await CallAsync("viewer", HttpMethod.Get, "template?kind=group");
        var forms = await CallAsync(Designer, HttpMethod.Get, "template");

        Assert.That(saved.Outcome, Is.EqualTo("201"), saved.ToString());
        var held = saved.Body!["template"]!["definition"]!["pages"]![0]!["groups"]![0]!;
        var fields = held["fields"]!.AsArray();
        Assert.Multiple(() =>
        {
            Assert.That((saved.Body!["rulesChanged"]!.GetValue<int>(), saved.Body!["template"]!["kind"]!.GetValue<string>()), Is.EqualTo((2, "group")), "the group's rule and the note's");
            Assert.That(held["rules"], Is.Null, "the group's rule named only a field outside it");
            Assert.That(fields[1]!["rules"]![0]!["conditions"]![0]!["field"]!.GetValue<Guid>(), Is.EqualTo(wants), "a rule inside stays");
            Assert.That(fields[2]!["rules"], Is.Null);
            Assert.That((fields[3]!["options"]!["parent"], fields[3]!["options"]!["items"]![0]!["parentValues"]), Is.EqualTo(((JsonNode?)null, (JsonNode?)null)), "the options without a parent outside");
            Assert.That((listed.Outcome, listed.Body!["total"]!.GetValue<int>(), listed.Body!["items"]![0]!["name"]!.GetValue<string>()), Is.EqualTo(("200", 1, "Address block")), "every user of the section lists the saved groups");
            Assert.That(forms.Body!["total"]!.GetValue<int>(), Is.EqualTo(BuiltInFormTemplates.All.Count), "a group is no form template");
        });
    }

    [Test]
    public async Task AGroupThatDoesNotRead_OrANameMissing_IsRefused_EachProblemAtItsPath()
    {
        var unread = await CallAsync(Designer, HttpMethod.Post, "template/group", new { name = "Broken", group = new { key = Guid.NewGuid(), fields = new[] { new { key = Guid.NewGuid(), alias = "x" } } } });
        var nameless = await CallAsync(Designer, HttpMethod.Post, "template/group", new { name = " ", group = new { key = Guid.NewGuid() } });

        Assert.Multiple(() =>
        {
            Assert.That(unread.Status, Is.EqualTo(System.Net.HttpStatusCode.BadRequest), unread.ToString());
            Assert.That(unread.Text, Does.Contain("\"$.group.fields[0]"), "the group's problems at $.group");
            Assert.That(nameless.Status, Is.EqualTo(System.Net.HttpStatusCode.BadRequest), nameless.ToString());
            Assert.That(nameless.Text, Does.Contain("\"$.name\""));
        });
    }

    [Test]
    public async Task SavingTakesManageLibrary_AndAFormsDraft_DesignOnTheForm()
    {
        var folder = await FolderAsync("Viewers' own");
        var form = await FormAsync("Theirs", folder);
        await RestrictAsync(folder, "viewer", FormBuilderPermissions.Design);
        var elsewhere = await FormAsync("Elsewhere");

        var withoutLibrary = await CallAsync("viewer", HttpMethod.Post, $"template/form/{form}", new { name = "Mine" });
        var withoutDesign = await CallAsync("viewer", HttpMethod.Post, $"template/form/{elsewhere}", new { name = "Mine" });
        var group = await CallAsync("viewer", HttpMethod.Post, "template/group", new { name = "Mine", group = new { key = Guid.NewGuid() } });

        Assert.Multiple(() =>
        {
            Assert.That(withoutLibrary.Status, Is.EqualTo(System.Net.HttpStatusCode.Forbidden), withoutLibrary.ToString());
            Assert.That(withoutLibrary.Text, Does.Contain("manageLibrary"));
            Assert.That(withoutDesign.Status, Is.EqualTo(System.Net.HttpStatusCode.NotFound).Or.EqualTo(System.Net.HttpStatusCode.Forbidden), withoutDesign.ToString());
            Assert.That(group.Status, Is.EqualTo(System.Net.HttpStatusCode.Forbidden), group.ToString());
        });
    }

    private static Dictionary<string, string> Text(string text) => new() { [""] = text };

    private static object Rule(Guid field) => new
    {
        key = Guid.NewGuid(),
        effect = "show",
        enabled = true,
        match = "all",
        conditions = new[] { new { field, op = "equals", value = "yes" } },
    };
}
