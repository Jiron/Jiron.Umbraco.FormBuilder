using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Forms;

/// <summary>
/// A group as a template holds it (ADR-0217): a condition naming an element outside the group, or comparing with a field
/// outside it, is left out, inside nested groups of conditions too; a nested group or a rule left empty goes; what points
/// inside stays, and each rule that changed counts once.
/// </summary>
[TestFixture]
public sealed class GroupTemplatesTests
{
    private const string Inside = "00000000-0000-4000-8000-000000000001";
    private const string Second = "00000000-0000-4000-8000-000000000002";
    private const string Outside = "00000000-0000-4000-8000-000000000099";

    [Test]
    public void OnlyWhatPointsInside_Stays()
    {
        var json = $$$"""
            {"schemaVersion":1,"key":"00000000-0000-4000-8000-0000000000a0","pages":[{"key":"00000000-0000-4000-8000-0000000000a1","groups":[{
              "key":"00000000-0000-4000-8000-0000000000a2",
              "fields":[
                {"key":"{{{Inside}}}","type":"number","alias":"first","label":{"":"First"}},
                {"key":"{{{Second}}}","type":"number","alias":"second","label":{"":"Second"},"rules":[
                  {"key":"00000000-0000-4000-8000-0000000000b1","effect":"show","match":"any","conditions":[
                    {"field":"{{{Inside}}}","op":"greaterThan","value":"1"},
                    {"match":"all","conditions":[{"field":"{{{Outside}}}","op":"equals","value":"yes"}]},
                    {"match":"all","conditions":[{"field":"{{{Inside}}}","op":"lessThan","value":{"field":"{{{Outside}}}"}},{"field":"{{{Inside}}}","op":"lessThan","value":"9"}]}
                  ]},
                  {"key":"00000000-0000-4000-8000-0000000000b2","effect":"require","conditions":[{"field":"{{{Inside}}}","op":"equals","value":{"field":"{{{Second}}}"}}]}
                ]}
              ],
              "rules":[{"key":"00000000-0000-4000-8000-0000000000b3","effect":"show","conditions":[{"field":"{{{Outside}}}","op":"equals","value":"yes"}]}]
            }]}]}
            """;
        var definition = DefinitionFixtures.Serializer().Read(json).Definition!;

        var (within, changed) = GroupTemplates.WithinItself(definition);
        var group = within.Pages.Single().Groups.Single();
        var rules = group.Fields[1].Rules;

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.EqualTo(2), "the field's first rule and the group's");
            Assert.That(group.Rules, Is.Empty, "the group's rule named only a field outside");
            Assert.That(rules.Select(rule => rule.Key.ToString()), Is.EqualTo(new[] { "00000000-0000-4000-8000-0000000000b1", "00000000-0000-4000-8000-0000000000b2" }));
            var kept = rules[0].Conditions;
            Assert.That(kept, Has.Count.EqualTo(2), "the nested group naming only the outside field went");
            Assert.That(kept[1] is RuleGroup { Conditions: [RuleCondition { Operator: "lessThan" }] }, Is.True, "the comparison with a field outside left its group");
            Assert.That(rules[1], Is.EqualTo(definition.Pages[0].Groups[0].Fields[1].Rules[1]), "a comparison inside stays as it was");
        });
    }
}
