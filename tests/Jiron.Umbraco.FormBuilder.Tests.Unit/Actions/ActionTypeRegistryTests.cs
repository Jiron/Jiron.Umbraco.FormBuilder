using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Actions;

/// <summary>
/// The action types' model (ROADMAP 8.1, ADR-0158): what a registry refuses, what the base class reads, the results an attempt can
/// give, and the host's locks of action settings as a site starts with them.
/// </summary>
[TestFixture]
public sealed class ActionTypeRegistryTests
{
    private const string Root = "JironFormBuilder:Designer:SettingLocks";

    private static readonly FormAction Sample = new() { Key = Guid.NewGuid(), Type = "acme.crm", Settings = SettingsObject.Parse("""{"address":"https://crm.example/","retries":3}""") };

    [Test]
    public void AFineType_MakesARegistry_ThatFindsItExactly()
    {
        var registry = new ActionTypeRegistry([new CrmType(), new CrmType("acme.other")]);

        Assert.Multiple(() =>
        {
            Assert.That(registry.Types.Select(type => type.Declaration.Alias), Is.EqualTo(new[] { "acme.crm", "acme.other" }), "in their order");
            Assert.That(registry.Find("acme.crm"), Is.Not.Null);
            Assert.That(registry.Find("ACME.CRM"), Is.Null, "an action names its type exactly");
            Assert.That(registry.Find("gone"), Is.Null);
        });
    }

    [TestCase("1bad", "alias outside the pattern")]
    [TestCase("", "alias outside the pattern")]
    [TestCase("with space", "alias outside the pattern")]
    public void AnAliasOutsideThePattern_IsRefused(string alias, string problem) =>
        Assert.That(ActionTypeRegistry.Problems([new CrmType(alias)]).Single(), Does.Contain(problem));

    [TestCase("acme.crm")]
    [TestCase("ACME.crm")]
    public void ASecondTypeOfAnAlias_IsRefused_AndSaysHowToReplaceTheFirst(string alias)
    {
        var problem = ActionTypeRegistry.Problems([new CrmType(), new CrmType(alias)]).Single();

        Assert.That(problem, Does.Contain("Replace<CrmType, CrmType>()"));
    }

    [Test]
    public void ADeclaration_WithoutAName_OrAnIcon_IsRefused()
    {
        var problems = ActionTypeRegistry.Problems([new Declared(new ActionTypeDeclaration { Alias = "acme.empty", Name = " ", Icon = "" })]);

        Assert.That(problems, Has.Count.EqualTo(2));
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(61)]
    public void ATimeLimit_BeyondAMinute_OrOfNothing_IsRefused(int seconds)
    {
        var problems = ActionTypeRegistry.Problems([new Declared(new ActionTypeDeclaration { Alias = "acme.limit", Name = "Limit", Icon = "icon-time", AttemptTimeout = TimeSpan.FromSeconds(seconds) })]);

        Assert.That(problems.Single(), Does.Contain("at most a minute"));
    }

    [TestCase(1)]
    [TestCase(60)]
    public void ATimeLimit_UpToAMinute_IsFine(int seconds) =>
        Assert.That(ActionTypeRegistry.Problems([new Declared(new ActionTypeDeclaration { Alias = "acme.limit", Name = "Limit", Icon = "icon-time", AttemptTimeout = TimeSpan.FromSeconds(seconds) })]), Is.Empty);

    [Test]
    public void ASettingVisitorsWouldSee_IsRefused() =>
        Assert.That(ActionTypeRegistry.Problems([new PublicType()]).Single(), Does.Contain("'shown'"));

    [Test]
    public void ATypeWithoutADeclaration_IsRefused() =>
        Assert.That(ActionTypeRegistry.Problems([new Undeclared()]).Single(), Does.Contain("declares nothing"));

    [Test]
    public void ARegistry_RefusesTypesWithProblems() =>
        Assert.That(() => new ActionTypeRegistry([new CrmType("1bad")]), Throws.InvalidOperationException.With.Message.Contains("1bad"));

    [Test]
    public void TheStartUpCheck_SaysWhatKeepsTheTypesFromBeingARegistry()
    {
        var bad = new ActionTypeStartupCheck(new FormBuilderActionTypeCollection(() => [new CrmType("1bad")])).Validate(null, new FormBuilderStartupChecks());
        var fine = new ActionTypeStartupCheck(new FormBuilderActionTypeCollection(() => [new CrmType()])).Validate(null, new FormBuilderStartupChecks());
        var cannot = new ActionTypeStartupCheck(new FormBuilderActionTypeCollection(() => throw new InvalidOperationException("no container"))).Validate(null, new FormBuilderStartupChecks());

        Assert.Multiple(() =>
        {
            Assert.That(bad.Failed, Is.True);
            Assert.That(bad.FailureMessage, Does.Contain("1bad"));
            Assert.That(fine.Succeeded, Is.True);
            Assert.That(cannot.FailureMessage, Does.Contain("no container"));
        });
    }

    [Test]
    public void TheBase_ReadsTheSettingsSchema_AndTheSettings()
    {
        var type = new CrmType();

        Assert.Multiple(() =>
        {
            Assert.That(type.SettingsSchema.Settings.Select(setting => setting.Name), Is.EqualTo(new[] { "address", "retries" }));
            Assert.That(type.ReadSettings(Sample.Settings).Address, Is.EqualTo("https://crm.example/"));
            Assert.That(type.ReadSettings(null).Retries, Is.EqualTo(1), "a new instance with its defaults for none");
            Assert.That(((IFormBuilderActionType)type).MissingConfiguration(Sample), Is.EqualTo(new[] { "JironFormBuilder:Secrets:Crm" }));
        });
    }

    [Test]
    public void TheBase_RefusesASettingsClassItCannotRead() =>
        Assert.That(() => new UnreadableType(), Throws.InvalidOperationException.With.Message.Contains("acme.unreadable"));

    [Test]
    public async Task TheBase_TestsAsADryRun_UnlessATypeSaysOtherwise()
    {
        var result = await ((IFormBuilderActionType)new CrmType()).TestAsync(
            new ActionTestContext
            {
                Action = Sample,
                User = new ActionTestUser(Guid.NewGuid(), null, "Ada"),
                Scope = new Core.MergeTags.MergeTagScope { Form = new FormDefinition { Key = Guid.NewGuid() }, FormName = "F", FormAlias = "f", Culture = string.Empty },
            },
            CancellationToken.None);

        Assert.That(result.Outcome, Is.EqualTo(ActionTestOutcome.DryRun));
    }

    [Test]
    public void AResult_TakesACodeWrittenAsACode()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ActionResult.Succeeded.Kind, Is.EqualTo(ActionResultKind.Succeeded));
            Assert.That(ActionResult.TryAgain("http_503", "No.") is { Kind: ActionResultKind.TryAgain, Code: "http_503", Message: "No." }, Is.True);
            Assert.That(ActionResult.Failed("refused") is { Kind: ActionResultKind.Failed, Code: "refused", Message: null }, Is.True);
            Assert.That(ActionResult.Uncertain("no.answer-1") is { Kind: ActionResultKind.Uncertain, Code: "no.answer-1" }, Is.True);
            Assert.That(() => ActionResult.TryAgain("Not A Code"), Throws.ArgumentException);
            Assert.That(() => ActionResult.Failed(""), Throws.ArgumentException);
            Assert.That(() => ActionResult.Uncertain(new string('a', 65)), Throws.ArgumentException);
        });
    }

    [Test]
    public void ASettingsCheck_ReportsThePlace_AndRefusesOneThatIsNone()
    {
        var problems = new DefinitionProblems();
        var check = new ActionSettingsCheck(Sample, new FormDefinition { Key = Guid.NewGuid() }, "$.actions[2].settings", problems);

        check.Invalid("mapping.field", "Choose a field.");

        Assert.Multiple(() =>
        {
            Assert.That(problems.ToReadOnlyList().Single(), Is.EqualTo(new DefinitionProblem(DefinitionErrors.SettingInvalid, "$.actions[2].settings.mapping.field", "Choose a field.")));
            Assert.That(() => check.Invalid("Not a place", "Message."), Throws.ArgumentException);
            Assert.That(() => check.Invalid("to", " "), Throws.ArgumentException);
        });
    }

    [Test]
    public void ALock_OfATypeTheSiteLacks_OrASettingItLacks_OrAValueItRefuses_StopsTheSite_NamingItsKeyAndNeverItsValue()
    {
        var registry = new ActionTypeRegistry([new CrmType()]);
        var problems = ActionSettingLocks.Problems(
            [
                Lock("acme.gone", "address", "JFBSECRET"),
                Lock("ACME.CRM", "address", "JFBSECRET"),
                Lock("acme.crm", "nothing", "JFBSECRET"),
                Lock("acme.crm", "retries", "JFBSECRET"),
                new DesignerSettingLock { Kind = "field", Type = "acme.gone", Setting = "nothing", Value = "of another kind" },
            ],
            registry);

        Assert.Multiple(() =>
        {
            Assert.That(problems, Has.Count.EqualTo(4));
            Assert.That(problems[0], Does.StartWith(Root + ":0:Type ('acme.gone') is not an action type of the site; its action types are acme.crm."));
            Assert.That(problems[1], Does.Contain("aliases are compared exactly, and the site has 'acme.crm'"));
            Assert.That(problems[2], Does.StartWith(Root + ":2:Setting ('nothing') is not a setting of the action type 'acme.crm'; its settings are"));
            Assert.That(problems[3], Does.StartWith(Root + ":3:Value is not a value of the setting 'retries' of the action type 'acme.crm'"));
            Assert.That(string.Join(' ', problems), Does.Not.Contain("JFBSECRET"));
        });
    }

    [Test]
    public void ALockOfASiteWithoutActionTypes_SaysItHasNone() =>
        Assert.That(ActionSettingLocks.Problems([Lock("acme.gone", "address", "x")], new ActionTypeRegistry([])).Single(), Does.Contain("which has none"));

    [Test]
    public void ARequiredSetting_KeptWithoutAValue_IsRefused()
    {
        var problems = ActionSettingLocks.Problems([new DesignerSettingLock { Kind = "action", Type = "acme.required", Setting = "needed" }], new ActionTypeRegistry([new RequiredType()]));

        Assert.That(problems.Single(), Does.Contain("an action cannot do without one"));
    }

    [Test]
    public void TheStartUpCheck_OfLocks_StopsTheSite_AndLeavesWhatOthersRefuse()
    {
        ActionSettingLockStartupCheck Check(params IFormBuilderActionType[] types)
        {
            var options = new FormBuilderOptions();
            options.Designer.SettingLocks.Add(Lock("acme.crm", "nothing", "x"));
            return new ActionSettingLockStartupCheck(Options.Create(options), new FormBuilderActionTypeCollection(() => types));
        }

        Assert.Multiple(() =>
        {
            Assert.That(Check(new CrmType()).Validate(null, new FormBuilderStartupChecks()).FailureMessage, Does.Contain(Root + ":0:Setting"));
            Assert.That(Check(new CrmType("1bad")).Validate(null, new FormBuilderStartupChecks()).Succeeded, Is.True, "the types' own check says that");
        });
    }

    [Test]
    public void ALock_AppliesTheValue_AndLeavesAnActionWithoutItsTypeAsItIs()
    {
        var locks = LocksOf(Lock("acme.crm", "address", "https://locked.example/"));
        var other = Sample with { Type = "acme.other" };

        var applied = locks.Apply(Sample);

        Assert.Multiple(() =>
        {
            Assert.That(applied.Settings!.Value.GetProperty("address").GetString(), Is.EqualTo("https://locked.example/"));
            Assert.That(applied.Settings!.Value.GetProperty("retries").GetInt32(), Is.EqualTo(3), "the other settings stay");
            Assert.That(locks.Apply(other), Is.SameAs(other));
        });
    }

    [Test]
    public void ALockWithoutAValue_LeavesTheSettingOut()
    {
        var locks = LocksOf(new DesignerSettingLock { Kind = "action", Type = "acme.crm", Setting = "address" });

        var applied = locks.Apply(Sample);

        Assert.That(applied.Settings!.TryGetProperty("address", out _), Is.False);
    }

    [Test]
    public void ALock_WithNothingToChange_GivesTheSameDefinitionBack()
    {
        var locks = LocksOf(Lock("acme.crm", "address", "https://crm.example/"));
        var none = LocksOf();
        var definition = new FormDefinition { Key = Guid.NewGuid(), Actions = [Sample with { Settings = SettingsObject.Parse("""{"address":"https://crm.example/"}""") }] };

        Assert.Multiple(() =>
        {
            Assert.That(locks.Apply(definition), Is.SameAs(definition));
            Assert.That(none.Apply(definition), Is.SameAs(definition));
        });
    }

    [Test]
    public void ALock_ChangesTheDefinition_ItApplies()
    {
        var locks = LocksOf(Lock("acme.crm", "address", "https://locked.example/"));
        var definition = new FormDefinition { Key = Guid.NewGuid(), Actions = [Sample, Sample with { Key = Guid.NewGuid(), Type = "acme.other" }] };

        var applied = locks.Apply(definition);

        Assert.Multiple(() =>
        {
            Assert.That(applied.Actions[0].Settings!.Value.GetProperty("address").GetString(), Is.EqualTo("https://locked.example/"));
            Assert.That(applied.Actions[1], Is.SameAs(definition.Actions[1]));
        });
    }

    [Test]
    public void ASave_IsHeldToTheLock_ButKeepsWhatTheDraftHeld_ForTheSameActionAndType()
    {
        var locks = LocksOf(Lock("acme.crm", "address", "https://locked.example/"));
        FormDefinition With(FormAction action) => new() { Key = Guid.NewGuid(), Actions = [action] };
        var given = Sample with { Settings = SettingsObject.Parse("""{"address":"https://mine.example/"}""") };
        var locked = Sample with { Settings = SettingsObject.Parse("""{"address":"https://locked.example/"}""") };

        Assert.Multiple(() =>
        {
            Assert.That(
                () => locks.CheckSave(With(given), null),
                Throws.TypeOf<FormBuilderSettingLockedException>().With.Property(nameof(FormBuilderSettingLockedException.Field)).EqualTo("$.actions[0].settings.address").And.Property(nameof(FormBuilderSettingLockedException.ConfigurationKey)).EqualTo(Root + ":0"));
            Assert.That(() => locks.CheckSave(With(locked), null), Throws.Nothing, "the lock's own value");
            Assert.That(() => locks.CheckSave(With(given with { Settings = null }), null), Throws.Nothing, "left out");
            Assert.That(() => locks.CheckSave(With(given), With(given)), Throws.Nothing, "kept from before the lock came");
            Assert.That(() => locks.CheckSave(With(given), With(given with { Type = "acme.other" })), Throws.TypeOf<FormBuilderSettingLockedException>(), "not of another type");
            Assert.That(() => locks.CheckSave(With(given), With(given with { Key = Guid.NewGuid() })), Throws.TypeOf<FormBuilderSettingLockedException>(), "not of another action");
            Assert.That(() => LocksOf().CheckSave(With(given), null), Throws.Nothing, "no lock");
        });
    }

    private static DesignerSettingLock Lock(string type, string setting, string value) => new() { Kind = "action", Type = type, Setting = setting, Value = value };

    private static ActionSettingLocks LocksOf(params DesignerSettingLock[] locks)
    {
        var options = new FormBuilderOptions();
        foreach (var settingLock in locks)
        {
            options.Designer.SettingLocks.Add(settingLock);
        }

        return new ActionSettingLocks(Options.Create(options), new ActionTypeRegistry([new CrmType()]));
    }

    private sealed class CrmSettings
    {
        [FormBuilderSetting("Address")]
        public string? Address { get; init; }

        [FormBuilderSetting("Retries")]
        public int Retries { get; init; } = 1;
    }

    private sealed class CrmType(string alias = "acme.crm") : FormBuilderActionTypeBase<CrmSettings>(new ActionTypeDeclaration { Alias = alias, Name = "CRM", Icon = "icon-handshake" })
    {
        protected override IReadOnlyList<string> MissingConfiguration(CrmSettings settings, FormAction action) => ["JironFormBuilder:Secrets:Crm"];

        protected override Task<ActionResult> RunAsync(CrmSettings settings, ActionRunContext context, CancellationToken cancellationToken) => Task.FromResult(ActionResult.Succeeded);
    }

    private sealed class Declared(ActionTypeDeclaration declaration) : FormBuilderActionTypeBase<NoFieldSettings>(declaration)
    {
        protected override Task<ActionResult> RunAsync(NoFieldSettings settings, ActionRunContext context, CancellationToken cancellationToken) => Task.FromResult(ActionResult.Succeeded);
    }

    private sealed class PublicSettings
    {
        [FormBuilderSetting("Shown", Public = true)]
        public string? Shown { get; init; }
    }

    private sealed class PublicType() : FormBuilderActionTypeBase<PublicSettings>(new ActionTypeDeclaration { Alias = "acme.public", Name = "Public", Icon = "icon-eye" })
    {
        protected override Task<ActionResult> RunAsync(PublicSettings settings, ActionRunContext context, CancellationToken cancellationToken) => Task.FromResult(ActionResult.Succeeded);
    }

    private sealed class RequiredSettings
    {
        [FormBuilderSetting("Needed")]
        [System.ComponentModel.DataAnnotations.Required]
        public string? Needed { get; init; }
    }

    private sealed class RequiredType() : FormBuilderActionTypeBase<RequiredSettings>(new ActionTypeDeclaration { Alias = "acme.required", Name = "Required", Icon = "icon-check" })
    {
        protected override Task<ActionResult> RunAsync(RequiredSettings settings, ActionRunContext context, CancellationToken cancellationToken) => Task.FromResult(ActionResult.Succeeded);
    }

    private sealed class UnreadableSettings
    {
        public string? NoAttribute { get; init; }
    }

    private sealed class UnreadableType() : FormBuilderActionTypeBase<UnreadableSettings>(new ActionTypeDeclaration { Alias = "acme.unreadable", Name = "Unreadable", Icon = "icon-wrong" })
    {
        protected override Task<ActionResult> RunAsync(UnreadableSettings settings, ActionRunContext context, CancellationToken cancellationToken) => Task.FromResult(ActionResult.Succeeded);
    }

    private sealed class Undeclared : IFormBuilderActionType
    {
        public ActionTypeDeclaration Declaration => null!;

        public FieldSettingsSchema SettingsSchema => FieldSettingsSchema.Empty;

        public void CheckSettings(ActionSettingsCheck check)
        {
        }

        public IReadOnlyList<string> MissingConfiguration(FormAction action) => [];

        public Task<ActionResult> RunAsync(ActionRunContext context, CancellationToken cancellationToken) => Task.FromResult(ActionResult.Succeeded);

        public Task<ActionTestResult> TestAsync(ActionTestContext context, CancellationToken cancellationToken) => Task.FromResult(new ActionTestResult(ActionTestOutcome.DryRun));
    }
}
