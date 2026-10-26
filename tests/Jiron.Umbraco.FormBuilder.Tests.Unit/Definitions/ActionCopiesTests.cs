using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Exchange;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;

/// <summary>
/// Copying actions to another form (ADR-0046): the copies and their rules get new keys, fields map by alias in any
/// letter case, grid rows by their label, other references to the source are kept and reported with their paths, and
/// references outside the form stay.
/// </summary>
public sealed class ActionCopiesTests
{
    private static readonly Guid SourcePage = Key(1);
    private static readonly Guid SourceEmail = Key(2);
    private static readonly Guid SourceTopic = Key(3);
    private static readonly Guid SourceGrid = Key(4);
    private static readonly Guid SourceVenue = Key(5);
    private static readonly Guid SourceFood = Key(6);
    private static readonly Guid Notify = Key(7);
    private static readonly Guid NotifyRule = Key(8);
    private static readonly Guid Hook = Key(9);
    private static readonly Guid HookRule = Key(10);
    private static readonly Guid Archive = Key(11);
    private static readonly Guid TargetEmail = Key(20);
    private static readonly Guid TargetTopic = Key(21);
    private static readonly Guid TargetGrid = Key(22);
    private static readonly Guid TargetFood = Key(23);
    private static readonly Guid TargetVenue = Key(24);
    private static readonly Guid TargetAction = Key(25);
    private static readonly Guid Media = Guid.Parse("74000000-0000-4000-8000-000000000001");

    [Test]
    public void CopiedActions_GoAfterTheTargetsOwn_WithNewKeys_AndFieldsMappedByAlias()
    {
        var result = Copy(Source(), [Notify, Hook], Target());
        var copy = result.Target!;
        var notify = copy.Actions[1];
        var hook = copy.Actions[2];

        Assert.Multiple(() =>
        {
            Assert.That(result.Problems, Is.Empty);
            Assert.That(copy.Actions.Select(action => action.Name), Is.EqualTo(new[] { "Own", "Notify", "Hook" }), "after the target's own, in the source's order");
            Assert.That((copy.Actions[0].Key, copy.Actions[0].Type), Is.EqualTo((TargetAction, "email")), "the target's own action stays as it is");
            Assert.That(result.Actions.Select(action => (action.SourceKey, action.Key)), Is.EqualTo(new[] { (Notify, notify.Key), (Hook, hook.Key) }));
            Assert.That(new[] { notify.Key, notify.Rules[0].Key, hook.Key, hook.Rules[0].Key }, Is.Unique, "new keys");
            Assert.That(new[] { notify.Key, notify.Rules[0].Key, hook.Key, hook.Rules[0].Key }.Intersect([Notify, NotifyRule, Hook, HookRule]), Is.Empty, "none of the source's");
            Assert.That(Conditions(notify).Select(condition => condition.Field), Is.EqualTo(new Guid?[] { TargetTopic, TargetEmail }), "fields by alias, in any letter case");
            Assert.That(Conditions(hook).Single().Field, Is.EqualTo(TargetVenue), "a grid row by its label");
            Assert.That(Setting(notify, "replyTo"), Is.EqualTo(TargetEmail.ToString("D")), "a setting that names a field");
            Assert.That(Setting(notify, "media"), Is.EqualTo(Media.ToString("D")), "a key of something outside the form stays");
            Assert.That(Setting(hook, "after"), Is.EqualTo(notify.Key.ToString("D")), "a reference to another copied action follows its copy");
            Assert.That((notify.Type, notify.Name, notify.Enabled, notify.Trigger), Is.EqualTo(("email", "Notify", true, ActionTrigger.Approved)), "everything else as it was");
            Assert.That(result.Actions[1].Unmapped, Is.Empty);
        });
    }

    [Test]
    public void AReference_WithoutAMatch_IsKeptAndReported_WithItsPathAndAlias()
    {
        var target = Target() with
        {
            Pages = [Target().Pages[0] with { Groups = [Target().Pages[0].Groups[0] with { Fields = [Target().Pages[0].Groups[0].Fields[0], Grid(TargetGrid, (TargetVenue, "Venue"), (TargetFood, "Venue"))] }] }],
        };

        var result = Copy(Source(), [Notify, Hook], target);
        var notify = result.Target!.Actions[1];

        Assert.Multiple(() =>
        {
            Assert.That(result.Actions[0].Unmapped, Is.EqualTo(new[]
            {
                new UnmappedReference("$.actions[1].rules[0].conditions[0].field", SourceTopic, "topic"),
                new UnmappedReference("$.actions[1].settings.page", SourcePage, null),
            }), "a field the target lacks, and an element that is no field");
            Assert.That(result.Actions[1].Unmapped, Is.EqualTo(new[] { new UnmappedReference("$.actions[2].rules[0].conditions[0].field", SourceVenue, "feedback") }), "a row whose label two rows have");
            Assert.That(Conditions(notify)[0].Field, Is.EqualTo(SourceTopic), "the reference is kept, so publishing refuses it");
            Assert.That(Conditions(notify)[1].Field, Is.EqualTo(TargetEmail), "the rest maps");
            Assert.That(Setting(notify, "page"), Is.EqualTo(SourcePage.ToString("D")));
        });
    }

    [Test]
    public void ARow_WithoutALabel_OrOfAFieldWithoutAMatch_IsReported()
    {
        var source = Source() with
        {
            Pages = [Source().Pages[0] with { Groups = [Source().Pages[0].Groups[0] with { Fields = [.. Source().Pages[0].Groups[0].Fields.SkipLast(1), Grid(SourceGrid, (SourceVenue, null), (SourceFood, "Food"))] }] }],
        };
        var target = Target() with
        {
            Pages = [Target().Pages[0] with { Groups = [Target().Pages[0].Groups[0] with { Fields = [.. Target().Pages[0].Groups[0].Fields.SkipLast(1), Grid(TargetGrid, (TargetVenue, null), (TargetFood, "Food"))] }] }],
        };

        var unlabelled = Copy(source, [Hook], target);
        var noGrid = Copy(Source(), [Hook], Target() with { Pages = [Target().Pages[0] with { Groups = [Target().Pages[0].Groups[0] with { Fields = [Target().Pages[0].Groups[0].Fields[0]] }] }] });

        Assert.Multiple(() =>
        {
            // The webhook also names the email action, which this copy leaves behind: an action is no field either.
            Assert.That(unlabelled.Actions[0].Unmapped.Select(reference => (reference.SourceKey, reference.Alias)), Is.EqualTo(new[] { (SourceVenue, (string?)"feedback"), (Notify, null) }));
            Assert.That(noGrid.Actions[0].Unmapped.Select(reference => (reference.SourceKey, reference.Alias)), Is.EqualTo(new[] { (SourceVenue, (string?)"feedback"), (Notify, null) }));
        });
    }

    [Test]
    public void CopyingToTheSourceItself_MapsEveryFieldToItself()
    {
        var source = Source();

        var result = Copy(source, [Notify], source);
        var copy = result.Target!.Actions[^1];

        Assert.Multiple(() =>
        {
            Assert.That(result.Target.Actions, Has.Count.EqualTo(source.Actions.Count + 1));
            Assert.That(Conditions(copy).Select(condition => condition.Field), Is.EqualTo(new Guid?[] { SourceTopic, SourceEmail }));
            Assert.That(copy.Key, Is.Not.EqualTo(Notify));
            Assert.That(result.Actions[0].Unmapped.Select(reference => reference.SourceKey), Is.EqualTo(new[] { SourcePage }), "a page of the same form is still no field");
        });
    }

    [Test]
    public void ATarget_WithoutActions_GetsTheCopies_AndNothingToCopy_ChangesNothing()
    {
        var target = Target() with { Actions = [] };

        var result = Copy(Source(), [Archive], target);
        var none = Copy(Source(), [], target);

        Assert.Multiple(() =>
        {
            Assert.That(result.Target!.Actions.Select(action => action.Name), Is.EqualTo(new[] { "Archive" }));
            Assert.That(FormDefinitionSerializer.WriteString(none.Target!), Is.EqualTo(FormDefinitionSerializer.WriteString(target)));
            Assert.That(none.Actions, Is.Empty);
        });
    }

    [Test]
    public void CopiesBeyondWhatAnySiteAllows_AreProblems_NotATarget()
    {
        var ceiling = DefinitionLimits.Ceiling.MaxActions;
        var target = Target() with { Actions = [.. Enumerable.Range(0, ceiling).Select(i => new FormAction { Key = Guid.NewGuid(), Type = "email", Name = $"Own {i}" })] };

        var result = Copy(Source(), [Archive], target);

        Assert.Multiple(() =>
        {
            Assert.That(result.Target, Is.Null);
            Assert.That(result.Problems.Select(problem => problem.Code), Has.Member(DefinitionErrors.LimitExceeded));
        });
    }

    [Test]
    public void AnAlias_TwoTargetFieldsHave_MapsToTheFirstOfThem()
    {
        var target = Target();
        var group = target.Pages[0].Groups[0];
        target = target with { Pages = [target.Pages[0] with { Groups = [group with { Fields = [.. group.Fields, Field(Key(26), "email", "EMAIL")] }] }] };

        var notify = Copy(Source(), [Notify], target).Target!.Actions[1];

        Assert.That(Conditions(notify)[1].Field, Is.EqualTo(TargetEmail), "a draft may give two fields one alias until it is published; the first in document order wins");
    }

    private static readonly IFormBuilderMergeTagService MergeTagService = MergeTagTestSite.Create().Service;

    private static ActionCopyResult Copy(FormDefinition source, Guid[] keys, FormDefinition target) => ActionCopies.Copy(
        source,
        keys.Select(key => source.Actions.Single(action => action.Key == key)).ToList(),
        target,
        DefinitionFixtures.Serializer(),
        text => MergeTagFieldPaths.Unknown(MergeTagService, text, target));

    private static Guid Key(int number) => Guid.Parse($"a0000000-0000-4000-8000-{number:D12}");

    private static FormField Field(Guid key, string type, string alias) => new() { Key = key, Type = type, Alias = alias, Label = new LocalizedText(alias) };

    private static FormField Grid(Guid key, params (Guid Key, string? Label)[] rows) => new()
    {
        Key = key,
        Type = "grid",
        Alias = "feedback",
        Label = new LocalizedText("Feedback"),
        Rows = [.. rows.Select(row => new GridRow { Key = row.Key, Label = row.Label is null ? null : new LocalizedText(row.Label) })],
    };

    private static FormDefinition Source() => new()
    {
        Key = Key(100),
        Pages =
        [
            new FormPage
            {
                Key = SourcePage,
                Groups = [new FormGroup { Key = Key(101), Fields = [Field(SourceEmail, "email", "email"), Field(SourceTopic, "radio", "topic"), Grid(SourceGrid, (SourceVenue, "Venue"), (SourceFood, "Food"))] }],
            },
        ],
        Actions =
        [
            new FormAction
            {
                Key = Notify,
                Type = "email",
                Name = "Notify",
                Trigger = ActionTrigger.Approved,
                Rules = [Run(NotifyRule, SourceTopic, SourceEmail)],
                Settings = Settings(new JsonObject { ["replyTo"] = SourceEmail.ToString("D"), ["page"] = SourcePage.ToString("D"), ["media"] = Media.ToString("D") }),
            },
            new FormAction { Key = Hook, Type = "webhook", Name = "Hook", Rules = [Run(HookRule, SourceVenue)], Settings = Settings(new JsonObject { ["after"] = Notify.ToString("D") }) },
            new FormAction { Key = Archive, Type = "webhook", Name = "Archive" },
        ],
    };

    private static FormDefinition Target() => new()
    {
        Key = Key(200),
        Pages =
        [
            new FormPage
            {
                Key = Key(201),
                Groups = [new FormGroup { Key = Key(202), Fields = [Field(TargetEmail, "email", "eMail"), Field(TargetTopic, "radio", "topic"), Grid(TargetGrid, (TargetFood, "Food"), (TargetVenue, "Venue"))] }],
            },
        ],
        Actions = [new FormAction { Key = TargetAction, Type = "email", Name = "Own" }],
    };

    private static FormRule Run(Guid key, params Guid[] fields) => new()
    {
        Key = key,
        Effect = RuleEffect.Run,
        Conditions = [.. fields.Select(field => (RuleItem)new RuleCondition { Field = field, Operator = "isNotEmpty" })],
    };

    private static SettingsObject Settings(JsonObject settings) => SettingsObject.Parse(settings.ToJsonString());

    private static List<RuleCondition> Conditions(FormAction action) => action.Rules[0].Conditions.Cast<RuleCondition>().ToList();

    private static string? Setting(FormAction action, string name) => JsonNode.Parse(action.Settings!.ToString())![name]?.GetValue<string>();
}
