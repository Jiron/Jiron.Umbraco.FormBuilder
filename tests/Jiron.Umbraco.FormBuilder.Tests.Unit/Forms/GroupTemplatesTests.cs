using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Forms;

/// <summary>
/// A group as a template holds it (ADR-0217): a rule or a validation rule that names an element outside the group, as a
/// condition's field or as the field a condition compares with, nested groups of conditions included, is left out whole
/// and counted; a validation rule on a field outside goes too; everything that names only the group stays as it was, a
/// rule without conditions included; and a list whose parent is outside keeps its options without the parent.
/// </summary>
[TestFixture]
public sealed class GroupTemplatesTests
{
    private const string Inside = "00000000-0000-4000-8000-000000000001";
    private const string Second = "00000000-0000-4000-8000-000000000002";
    private const string Third = "00000000-0000-4000-8000-000000000003";
    private const string Outside = "00000000-0000-4000-8000-000000000099";

    private static readonly string Json = $$$"""
        {"schemaVersion":1,"key":"00000000-0000-4000-8000-0000000000a0","pages":[{"key":"00000000-0000-4000-8000-0000000000a1","groups":[{
          "key":"00000000-0000-4000-8000-0000000000a2",
          "fields":[
            {"key":"{{{Inside}}}","type":"number","alias":"first","label":{"":"First"}},
            {"key":"{{{Second}}}","type":"number","alias":"second","label":{"":"Second"},"rules":[
              {"key":"00000000-0000-4000-8000-0000000000b1","effect":"show","match":"any","conditions":[
                {"field":"{{{Inside}}}","op":"greaterThan","value":"1"},
                {"match":"all","conditions":[{"field":"{{{Inside}}}","op":"lessThan","value":{"field":"{{{Outside}}}"}}]}
              ]},
              {"key":"00000000-0000-4000-8000-0000000000b2","effect":"require","conditions":[{"field":"{{{Inside}}}","op":"equals","value":{"field":"{{{Second}}}"}}]},
              {"key":"00000000-0000-4000-8000-0000000000b4","effect":"show","match":"all","conditions":[]}
            ]},
            {"key":"{{{Third}}}","type":"dropdown","alias":"third","label":{"":"Third"},"options":{"parent":"{{{Outside}}}","items":[
              {"value":"a","label":{"":"A"},"parentValues":["x"]},
              {"value":"b","label":{"":"B"},"parentValues":["y"]}
            ]}}
          ],
          "rules":[{"key":"00000000-0000-4000-8000-0000000000b3","effect":"show","conditions":[{"field":"{{{Outside}}}","op":"equals","value":"yes"}]}]
        }]}],
        "validationRules":[
          {"key":"00000000-0000-4000-8000-0000000000c1","field":"{{{Second}}}","message":{"":"Second must be more"},"conditions":[{"field":"{{{Second}}}","op":"lessThan","value":{"field":"{{{Inside}}}"}}]},
          {"key":"00000000-0000-4000-8000-0000000000c2","field":"{{{Second}}}","message":{"":"Too many"},"conditions":[{"field":"{{{Outside}}}","op":"equals","value":"yes"}]},
          {"key":"00000000-0000-4000-8000-0000000000c3","field":"{{{Outside}}}","message":{"":"Elsewhere"},"conditions":[{"field":"{{{Inside}}}","op":"isEmpty"}]}
        ]}
        """;

    [Test]
    public void ARuleNamingAnElementOutside_IsLeftOutWhole_AndWhatNamesOnlyTheGroup_StaysAsItWas()
    {
        var definition = DefinitionFixtures.Serializer().Read(Json).Definition!;

        var (within, rulesLeftOut, _) = GroupTemplates.WithinItself(definition);
        var group = within.Pages.Single().Groups.Single();
        var rules = group.Fields[1].Rules;

        Assert.Multiple(() =>
        {
            Assert.That(rulesLeftOut, Is.EqualTo(2), "the field's first rule, whose nested group compares with a field outside, and the group's");
            Assert.That(group.Rules, Is.Empty, "the group's rule named a field outside");
            Assert.That(rules.Select(rule => rule.Key.ToString()), Is.EqualTo(new[] { "00000000-0000-4000-8000-0000000000b2", "00000000-0000-4000-8000-0000000000b4" }));
            Assert.That(rules[0], Is.EqualTo(definition.Pages[0].Groups[0].Fields[1].Rules[1]), "a comparison inside stays as it was");
            Assert.That(rules[1].Conditions, Is.Empty, "a rule without conditions names nothing outside, and stays");
        });
    }

    [Test]
    public void AValidationRuleOnTheGroupsFields_NamingOnlyThem_Stays_AndOneNamingAFieldOutside_IsLeftOut()
    {
        var definition = DefinitionFixtures.Serializer().Read(Json).Definition!;

        var (within, _, checksLeftOut) = GroupTemplates.WithinItself(definition);

        Assert.Multiple(() =>
        {
            Assert.That(within.ValidationRules.Select(rule => rule.Key.ToString()), Is.EqualTo(new[] { "00000000-0000-4000-8000-0000000000c1" }));
            Assert.That(within.ValidationRules[0], Is.EqualTo(definition.ValidationRules[0]), "kept as it was");
            Assert.That(checksLeftOut, Is.EqualTo(2), "one compares with a field outside, one shows its message outside");
        });
    }

    [Test]
    public void AListWhoseParentIsOutside_KeepsItsOptions_WithoutTheParent()
    {
        var definition = DefinitionFixtures.Serializer().Read(Json).Definition!;

        var options = GroupTemplates.WithinItself(definition).Definition.Pages[0].Groups[0].Fields[2].Options!;

        Assert.Multiple(() =>
        {
            Assert.That(options.Parent, Is.Null);
            Assert.That(options.Items.Select(item => item.Value), Is.EqualTo(new[] { "a", "b" }));
            Assert.That(options.Items.All(item => item.ParentValues.Count == 0), Is.True, "every option shows");
        });
    }
}
