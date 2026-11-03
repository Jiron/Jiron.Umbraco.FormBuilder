using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Exchange;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;

/// <summary>What a definition holds, as the import's check and the gallery say it (ROADMAP 9.2, ADR-0166).</summary>
[TestFixture]
public sealed class DefinitionCountsTests
{
    private static FormRule Rule() => new() { Key = Guid.NewGuid(), Effect = RuleEffect.Show };

    [Test]
    public void TheCounts_TakeEveryPage_EveryField_WithARepeatersChildren_EveryRule_TheOutcomes_AndTheActions()
    {
        var child = new FormField { Key = Guid.NewGuid(), Type = "text", Alias = "guestName", Rules = [Rule()] };
        var definition = new FormDefinition
        {
            Key = Guid.NewGuid(),
            Pages =
            [
                new FormPage
                {
                    Key = Guid.NewGuid(),
                    Rules = [Rule()],
                    Groups =
                    [
                        new FormGroup
                        {
                            Key = Guid.NewGuid(),
                            Rules = [Rule()],
                            Fields = [new FormField { Key = Guid.NewGuid(), Type = "text", Alias = "name", Rules = [Rule(), Rule()] }, new FormField { Key = Guid.NewGuid(), Type = "repeater", Alias = "guests", Fields = [child] }],
                        },
                    ],
                },
                new FormPage { Key = Guid.NewGuid(), Groups = [new FormGroup { Key = Guid.NewGuid() }] },
            ],
            ValidationRules = [new FormValidationRule { Key = Guid.NewGuid(), Field = child.Key }],
            Actions = [new FormAction { Key = Guid.NewGuid(), Type = "email", Rules = [Rule()] }, new FormAction { Key = Guid.NewGuid(), Type = "webhook" }],
            Outcomes = [new FormOutcome { Key = Guid.NewGuid(), Rules = [Rule()] }],
        };

        Assert.Multiple(() =>
        {
            Assert.That(DefinitionCounts.Of(definition), Is.EqualTo(new DefinitionCounts(Pages: 2, Fields: 3, Rules: 8, Outcomes: 1, Actions: 2)));
            Assert.That(() => DefinitionCounts.Of(null!), Throws.ArgumentNullException);
        });
    }
}
