using System.Reflection;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Upgrades;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Configuration;

/// <summary>
/// The host configuration (section 4.8, ADR-0008, ADR-0042): its defaults, the binding of every key, the validator's
/// checks with the message each failure gives, and a site with an invalid configuration that does not start.
/// </summary>
[TestFixture]
public sealed class FormBuilderOptionsTests
{
    private const string Root = FormBuilderOptions.SectionName;
    private const string Limits = Root + ":Limits:Definitions:";
    private const string Locks = Root + ":Designer:SettingLocks:";
    private const string Grants = Root + ":Access:Grants:";

    [Test]
    public void TheDefaults_AreSection48s_AndPassTheValidator()
    {
        var options = new FormBuilderOptions();

        Assert.Multiple(() =>
        {
            Assert.That(FormBuilderOptions.SectionName, Is.EqualTo("JironFormBuilder"));
            Assert.That(options.Files.Root, Is.EqualTo("~/umbraco/Data/JironFormBuilder/Uploads"));
            Assert.That(options.Files.MaxFileBytes, Is.EqualTo(52_428_800), "50 MiB");
            Assert.That(options.Files.BlockedExtensions, Is.Empty, "the defaults apply while a site lists none");
            Assert.That(FormBuilderFilesOptions.DefaultBlockedExtensions, Is.EqualTo(new[] { "exe", "dll", "js", "bat", "cmd", "ps1", "sh", "msi", "html", "svg" }));
            Assert.That(options.Limits.Definitions, Is.EqualTo(new DefinitionLimits()));
            Assert.That(options.Designer.SettingLocks, Is.Empty);
            Assert.That(options.LockedSections, Is.Empty);
            Assert.That(options.Webhooks.AllowedHosts, Is.Empty, "no other host is called");
            Assert.That(options.Webhooks.AllowHttp, Is.False);
            Assert.That(HostOptions.Failures(options), Is.Empty);
        });
    }

    [Test]
    public void TheDefinitionLimits_DefaultToSection431s()
    {
        var limits = new DefinitionLimits();

        Assert.Multiple(() =>
        {
            Assert.That(limits.MaxBytes, Is.EqualTo(2 * 1024 * 1024));
            Assert.That(limits.MaxPages, Is.EqualTo(30));
            Assert.That(limits.MaxGroups, Is.EqualTo(100));
            Assert.That(limits.MaxFields, Is.EqualTo(300));
            Assert.That(limits.MaxOptionsPerField, Is.EqualTo(1000));
            Assert.That(limits.MaxActions, Is.EqualTo(50));
            Assert.That(limits.MaxOutcomes, Is.EqualTo(50));
            Assert.That(limits.MaxRules, Is.EqualTo(200));
        });
    }

    [TestCaseSource(nameof(LimitNames))]
    public void TheCeiling_IsTenTimesEachDefault(string name)
    {
        Assert.That(DefinitionLimits.CeilingFactor, Is.EqualTo(10));
        Assert.That(Limit(name, DefinitionLimits.Ceiling), Is.EqualTo(10 * Limit(name, new DefinitionLimits())));
    }

    [Test]
    public void EveryKey_Binds_AndPasses()
    {
        var settings = new Dictionary<string, string?>
        {
            [$"{Root}:Files:Root"] = "~/App_Data/forms",
            [$"{Root}:Files:MaxFileBytes"] = "1000",
            [$"{Root}:Files:BlockedExtensions:0"] = "dwg",
            [$"{Root}:Files:BlockedExtensions:1"] = "exe",
            [$"{Limits}MaxBytes"] = "1000",
            [$"{Limits}MaxPages"] = "2",
            [$"{Limits}MaxGroups"] = "3",
            [$"{Limits}MaxFields"] = "4",
            [$"{Limits}MaxOptionsPerField"] = "5",
            [$"{Limits}MaxActions"] = "6",
            [$"{Limits}MaxOutcomes"] = "7",
            [$"{Limits}MaxRules"] = "8",
            [$"{Locks}0:Kind"] = "action",
            [$"{Locks}0:Type"] = "email",
            [$"{Locks}0:Setting"] = "from",
            [$"{Locks}0:Value"] = "noreply@example.com",
            [$"{Locks}0:Hidden"] = "true",
            [$"{Locks}1:Kind"] = "field",
            [$"{Locks}1:Type"] = "text",
            [$"{Locks}1:Setting"] = "maxLength",
            [$"{Root}:LockedSections:0"] = "privacy",
            [$"{Root}:LockedSections:1"] = "Email",
            [$"{Root}:Webhooks:AllowedHosts:0"] = "events.example.com",
            [$"{Root}:Webhooks:AllowHttp"] = "true",
        };

        var options = HostOptions.Bind(settings);

        Assert.Multiple(() =>
        {
            Assert.That(options.Files.Root, Is.EqualTo("~/App_Data/forms"));
            Assert.That((options.Files.MaxFileBytes, options.Files.BlockedExtensions), Is.EqualTo((1000L, new[] { "dwg", "exe" })), "the site's own list, without the defaults");
            Assert.That(
                options.Limits.Definitions,
                Is.EqualTo(new DefinitionLimits { MaxBytes = 1000, MaxPages = 2, MaxGroups = 3, MaxFields = 4, MaxOptionsPerField = 5, MaxActions = 6, MaxOutcomes = 7, MaxRules = 8 }));
            Assert.That(
                options.Designer.SettingLocks.Select(l => (l.Kind, l.Type, l.Setting, l.Value, l.Hidden)),
                Is.EqualTo(new[] { ("action", "email", "from", "noreply@example.com", true), ("field", "text", "maxLength", (string?)null, false) }));
            Assert.That(options.LockedSections, Is.EqualTo(new[] { "privacy", "Email" }));
            Assert.That((options.Webhooks.AllowedHosts.Single(), options.Webhooks.AllowHttp), Is.EqualTo(("events.example.com", true)));
            Assert.That(HostOptions.Failures(settings), Is.Empty);
        });
    }

    [TestCaseSource(nameof(LimitNames))]
    public void ALimit_TakesOneToTenTimesItsDefault(string name)
    {
        var highest = Limit(name, DefinitionLimits.Ceiling);
        var failure = $"{Limits}{name} ({{0}}) must be 1 to {highest}, which is 10 times its default.";

        Assert.Multiple(() =>
        {
            Assert.That(LimitFailures(name, 1), Is.Empty, "1");
            Assert.That(LimitFailures(name, highest), Is.Empty, "the ceiling");
            Assert.That(LimitFailures(name, 0), Is.EqualTo(new[] { string.Format(null, failure, 0) }));
            Assert.That(LimitFailures(name, -1), Is.EqualTo(new[] { string.Format(null, failure, -1) }));
            Assert.That(LimitFailures(name, highest + 1), Is.EqualTo(new[] { string.Format(null, failure, highest + 1) }));
        });
    }

    [TestCase("field")]
    [TestCase("action")]
    [TestCase("Field")]
    [TestCase("ACTION")]
    public void ALock_TakesEitherKind_InAnyCase(string kind)
    {
        Assert.That(HostOptions.Failures(Lock(kind: kind)), Is.Empty);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("fields")]
    [TestCase("1")]
    public void ALock_RefusesAnotherKind(string? kind)
    {
        Assert.That(HostOptions.Failures(Lock(kind: kind)), Is.EqualTo(new[] { $"{Locks}0:Kind ('{kind}') must be 'field' or 'action'." }));
    }

    [TestCase("email")]
    [TestCase("e")]
    [TestCase("E")]
    [TestCase("acme.rating-v2_x")]
    public void ALock_TakesATypeAlias(string type)
    {
        Assert.That(HostOptions.Failures(Lock(type: type)), Is.Empty);
        Assert.That(HostOptions.Failures(Lock(type: type[0] + new string('a', 63))), Is.Empty, "64 characters");
    }

    [TestCaseSource(nameof(BadTypes))]
    public void ALock_RefusesAnythingElseAsItsType(string? type)
    {
        Assert.That(
            HostOptions.Failures(Lock(type: type)),
            Is.EqualTo(new[] { $"{Locks}0:Type ('{type}') must be a type's alias, such as 'email': a letter, then letters, digits, '.', '-' and '_', at most 64 characters." }));
    }

    [TestCase("from")]
    [TestCase("replyTo")]
    [TestCase("a1")]
    public void ALock_TakesASettingName(string setting)
    {
        Assert.That(HostOptions.Failures(Lock(setting: setting)), Is.Empty);
        Assert.That(HostOptions.Failures(Lock(setting: setting[0] + new string('A', 63))), Is.Empty, "64 characters");
    }

    [TestCaseSource(nameof(BadSettings))]
    public void ALock_RefusesAnythingElseAsItsSetting(string? setting)
    {
        Assert.That(
            HostOptions.Failures(Lock(setting: setting)),
            Is.EqualTo(new[] { $"{Locks}0:Setting ('{setting}') must be a setting's name, such as 'from': a lowercase letter, then letters and digits, at most 64 characters." }));
    }

    [Test]
    public void ALocksValue_HasAtMost50000Characters_AndIsNeverRepeated()
    {
        var longest = new string('v', FormBuilderOptionsValidator.MaxLockValueLength);
        var failures = HostOptions.Failures(Lock(value: longest + "w"));

        Assert.Multiple(() =>
        {
            Assert.That(HostOptions.Failures(Lock(value: longest)), Is.Empty);
            Assert.That(HostOptions.Failures(Lock(value: string.Empty)), Is.Empty, "an empty value");
            Assert.That(failures, Is.EqualTo(new[] { $"{Locks}0:Value has 50001 characters; a lock's value has at most 50000." }));
            Assert.That(failures.Single(), Does.Not.Contain("vvvv"));
        });
    }

    [Test]
    public void ALock_OfAKindTypeAndSetting_IsGivenOnce()
    {
        var settings = Lock(kind: "action");
        foreach (var (key, value) in Lock(index: 1, kind: "Action", value: "other@example.com"))
        {
            settings[key] = value;
        }

        Assert.That(
            HostOptions.Failures(settings),
            Is.EqualTo(new[] { $"{Locks}1 locks the setting 'from' of the action type 'email' again; {Locks}0 locks it already. Keep one." }));
    }

    [Test]
    public void LocksOfAnotherKindTypeOrSetting_AreNoRepeats()
    {
        var settings = Lock(kind: "action", type: "email", setting: "from");
        string?[][] others = [["field", "email", "from"], ["action", "webhook", "from"], ["action", "email", "to"]];
        for (var i = 0; i < others.Length; i++)
        {
            foreach (var (key, value) in Lock(index: i + 1, kind: others[i][0], type: others[i][1], setting: others[i][2]))
            {
                settings[key] = value;
            }
        }

        Assert.That(HostOptions.Failures(settings), Is.Empty);
    }

    [Test]
    public void InvalidLocks_AreNotComparedForRepeats()
    {
        var settings = Lock(kind: null);
        foreach (var (key, value) in Lock(index: 1, kind: null))
        {
            settings[key] = value;
        }

        Assert.That(HostOptions.Failures(settings), Has.Count.EqualTo(2).And.All.Contains(":Kind ('') must be"));
    }

    [Test]
    public void AnEmptyLock_IsRefused()
    {
        var options = new FormBuilderOptions();
        options.Designer.SettingLocks.Add(null!);

        Assert.That(HostOptions.Failures(options), Is.EqualTo(new[] { $"{Locks}0 is empty: give its Kind, Type and Setting." }));
    }

    [Test]
    public void EverySection_CanBeLocked_InAnyCase()
    {
        var options = new FormBuilderOptions();
        foreach (var section in FormBuilderSettingsSections.Lockable)
        {
            options.LockedSections.Add(section);
            options.LockedSections.Add(section.ToUpperInvariant());
        }

        Assert.Multiple(() =>
        {
            Assert.That(FormBuilderSettingsSections.All, Is.EqualTo(new[] { "general", "email", "protection", "files", "privacy", "integrations" }));
            Assert.That(FormBuilderSettingsSections.Lockable, Is.EqualTo(new[] { "general", "email", "protection", "files", "privacy", "access", "integrations" }));
            Assert.That(HostOptions.Failures(options), Is.Empty);
        });
    }

    [Test]
    public void TheFileLimits_AreAFileSizeWithinItsBounds_AndExtensionsInLowercase_EachOnce()
    {
        var settings = new Dictionary<string, string?>
        {
            [$"{Root}:Files:MaxFileBytes"] = "0",
            [$"{Root}:Files:BlockedExtensions:0"] = "exe",
            [$"{Root}:Files:BlockedExtensions:1"] = ".js",
            [$"{Root}:Files:BlockedExtensions:2"] = "EXE",
            [$"{Root}:Files:BlockedExtensions:3"] = "abcdefghijk",
            [$"{Root}:Files:BlockedExtensions:4"] = "exe",
            [$"{Root}:Files:BlockedExtensions:5"] = "7z",
        };

        Assert.Multiple(() =>
        {
            Assert.That(HostOptions.Failures(settings), Is.EqualTo(new[]
            {
                $"{Root}:Files:MaxFileBytes (0) must be 1 to 999999999999999.",
                $"{Root}:Files:BlockedExtensions:1 ('.js') must be an extension in lowercase letters and digits without the point, at most 10 of them, such as 'exe'.",
                $"{Root}:Files:BlockedExtensions:2 ('EXE') must be an extension in lowercase letters and digits without the point, at most 10 of them, such as 'exe'.",
                $"{Root}:Files:BlockedExtensions:3 ('abcdefghijk') must be an extension in lowercase letters and digits without the point, at most 10 of them, such as 'exe'.",
                $"{Root}:Files:BlockedExtensions:4 ('exe') names an extension again; {Root}:Files:BlockedExtensions:0 names it already. Keep one.",
            }));
            Assert.That(HostOptions.Failures(new Dictionary<string, string?> { [$"{Root}:Files:MaxFileBytes"] = "1000000000000000" }), Has.One.StartsWith($"{Root}:Files:MaxFileBytes (1000000000000000) must be"));
            Assert.That(HostOptions.Failures(new Dictionary<string, string?> { [$"{Root}:Files:MaxFileBytes"] = "999999999999999", [$"{Root}:Files:BlockedExtensions:0"] = "abcdefghij" }), Is.Empty, "the highest limit, the longest extension");
            Assert.That(HostOptions.Failures(new Dictionary<string, string?> { [$"{Root}:Files:MaxFileBytes"] = "1" }), Is.Empty);
        });
    }

    [Test]
    public void ALockedSection_MustBeASection()
    {
        Assert.That(
            HostOptions.Failures(new Dictionary<string, string?> { [$"{Root}:LockedSections:0"] = "privacy", [$"{Root}:LockedSections:1"] = "spam" }),
            Is.EqualTo(new[] { $"{Root}:LockedSections:1 ('spam') is not a settings section; use one of general, email, protection, files, privacy, access, integrations." }));
    }

    [Test]
    public void HostGrants_NameEachGroupOnce_WithKnownPermissions_InAnyCase()
    {
        var settings = new Dictionary<string, string?>
        {
            [$"{Grants}0:Group"] = "editor",
            [$"{Grants}0:Permissions:0"] = "VIEWENTRIES",
            [$"{Grants}0:Permissions:1"] = "export",
            [$"{Grants}1:Group"] = "jfbDesigners",
            [$"{Grants}1:Permissions:0"] = "design",
            [$"{Grants}1:Permissions:1"] = "manageLibrary",
        };

        var options = HostOptions.Bind(settings);

        Assert.Multiple(() =>
        {
            Assert.That(new FormBuilderOptions().Access.Grants, Is.Empty, "no host grants by default");
            Assert.That(options.Access.Grants.Select(grant => grant.Group), Is.EqualTo(new[] { "editor", "jfbDesigners" }));
            Assert.That(options.Access.Grants[0].Permissions, Is.EqualTo(new[] { "VIEWENTRIES", "export" }));
            Assert.That(HostOptions.Failures(settings), Is.Empty);
        });
    }

    [Test]
    public void AHostGrant_ThatIsNotWhole_IsRefused_NamingItsKey()
    {
        var failures = HostOptions.Failures(new Dictionary<string, string?>
        {
            [$"{Grants}0:Permissions:0"] = "viewEntries",
            [$"{Grants}1:Group"] = "editor",
            [$"{Grants}2:Group"] = "writer",
            [$"{Grants}2:Permissions:0"] = "viewEntries",
            [$"{Grants}2:Permissions:1"] = "viewEntry",
            [$"{Grants}3:Group"] = "EDITOR",
            [$"{Grants}3:Permissions:0"] = "export",
            [$"{Grants}3:Permissions:1"] = "manageActions",
        });

        Assert.That(failures, Is.EqualTo(new[]
        {
            $"{Grants}0:Group is missing: name a user group by its alias, such as 'editor'.",
            $"{Grants}1:Permissions is empty: list at least one permission, or remove the grant.",
            $"{Grants}2:Permissions:1 ('viewEntry') is not a permission; use one of design, manageActions, viewEntries, viewSensitive, editEntries, deleteEntries, export, manageLibrary, manageSettings, managePermissions, privacyTools.",
            $"{Grants}3:Group ('EDITOR') is granted again; {Grants}1 grants it already. Keep one.",
            $"{Grants}3:Permissions grants 'manageActions' without 'design', which it needs.",
            $"{Grants}3:Permissions grants 'export' without 'viewEntries', which it needs.",
        }));
    }

    [Test]
    public void AnEmptyHostGrant_IsRefused()
    {
        var options = new FormBuilderOptions();
        options.Access.Grants.Add(null!);

        Assert.That(HostOptions.Failures(options), Is.EqualTo(new[] { $"{Grants}0 is empty: give its Group and Permissions." }));
    }

    [Test]
    public void ASiteWithAnInvalidHostGrant_DoesNotStart_AndSaysWhatToFix()
    {
        using var services = HostOptions.Services(new Dictionary<string, string?>
        {
            [$"{Grants}0:Group"] = "editor",
            [$"{Grants}0:Permissions:0"] = "viewSensitive",
            [$"{Root}:Access:Grant:0:Group"] = "writer",
        });

        Assert.That(
            () => HostOptions.Start(services),
            Throws.InstanceOf<OptionsValidationException>()
                .With.Message.Contains($"{Grants}0:Permissions grants 'viewSensitive' without 'viewEntries', which it needs.")
                .And.Message.Contains("JironFormBuilder:Access:Grant is not a setting of Jiron Form Builder; the settings in JironFormBuilder:Access are Grants."));
    }

    [Test]
    public void TheFilesRoot_IsChecked()
    {
        var options = new FormBuilderOptions { Files = { Root = Path.Combine(HostOptions.WebRoot, "uploads") } };

        Assert.That(HostOptions.Failures(options), Has.One.Contains("JironFormBuilder:Files:Root lies inside the web root"));
    }

    [Test]
    public void OnlyTheSitesOwnOptions_AreChecked()
    {
        var validator = new FormBuilderOptionsValidator(HostOptions.Folders(), HostOptions.Configuration().GetSection(Root));
        var invalid = new FormBuilderOptions();
        invalid.LockedSections.Add("spam");

        Assert.Multiple(() =>
        {
            Assert.That(validator.Validate("other", invalid).Skipped, Is.True, "named options are not the site's");
            Assert.That(validator.Validate(null, invalid).Failed, Is.True);
            Assert.That(validator.Validate(Options.DefaultName, invalid).Failed, Is.True);
        });
    }

    [Test]
    public void EveryProblem_IsListed_InOneResult()
    {
        var failures = HostOptions.Failures(new Dictionary<string, string?>
        {
            [$"{Root}:Limit:MaxFields"] = "5",
            [$"{Limits}MaxPages"] = "0",
            [$"{Locks}0:Kind"] = "form",
            [$"{Locks}0:Type"] = "email",
            [$"{Locks}0:Setting"] = "from",
            [$"{Root}:LockedSections:0"] = "spam",
        });

        Assert.That(failures, Has.Count.EqualTo(4));
    }

    [TestCase("JironFormBuilder:Limit:Definitions:MaxFields", "JironFormBuilder:Limit is not a setting of Jiron Form Builder; the settings in JironFormBuilder are PublicApi, Files, Limits, Designer, Access, Webhooks and LockedSections.")]
    [TestCase("JironFormBuilder:Limits:Definitions:MaxField", "JironFormBuilder:Limits:Definitions:MaxField is not a setting of Jiron Form Builder; the settings in JironFormBuilder:Limits:Definitions are MaxBytes, MaxPages, MaxGroups, MaxFields, MaxOptionsPerField, MaxActions, MaxOutcomes and MaxRules.")]
    [TestCase("JironFormBuilder:Limits:Definition:MaxFields", "JironFormBuilder:Limits:Definition is not a setting of Jiron Form Builder; the settings in JironFormBuilder:Limits are Definitions.")]
    [TestCase("JironFormBuilder:LockedSection:0", "JironFormBuilder:LockedSection is not a setting of Jiron Form Builder; the settings in JironFormBuilder are PublicApi, Files, Limits, Designer, Access, Webhooks and LockedSections.")]
    [TestCase("JironFormBuilder:Designer:SettingLocks:0:Typ", "JironFormBuilder:Designer:SettingLocks:0:Typ is not a setting of Jiron Form Builder; the settings in JironFormBuilder:Designer:SettingLocks:0 are Kind, Type, Setting, Value and Hidden.")]
    [TestCase("JironFormBuilder:Files:Root:Folder", "JironFormBuilder:Files:Root is a single value, not a section.")]
    [TestCase("JironFormBuilder:LockedSections:0:Name", "JironFormBuilder:LockedSections:0 is a single value, not a section.")]
    [TestCase("JironFormBuilder:Limits", "JironFormBuilder:Limits is a section of settings, not a single value.")]
    [TestCase("JironFormBuilder:LockedSections", "JironFormBuilder:LockedSections is a list: give its items as JironFormBuilder:LockedSections:0, JironFormBuilder:LockedSections:1 and so on.")]
    public void AKeyTheOptionsDoNotHave_IsRefused(string key, string failure)
    {
        Assert.That(HostOptions.Failures(new Dictionary<string, string?> { [key] = "privacy" }), Has.One.EqualTo(failure));
    }

    [Test]
    public void Keys_MatchInAnyCase_AsConfigurationDoes()
    {
        var settings = new Dictionary<string, string?>
        {
            ["jironformbuilder:limits:definitions:maxfields"] = "5",
            ["JIRONFORMBUILDER:DESIGNER:SETTINGLOCKS:0:KIND"] = "field",
            ["JIRONFORMBUILDER:DESIGNER:SETTINGLOCKS:0:TYPE"] = "text",
            ["JIRONFORMBUILDER:DESIGNER:SETTINGLOCKS:0:SETTING"] = "maxLength",
        };

        Assert.Multiple(() =>
        {
            Assert.That(HostOptions.Bind(settings).Limits.Definitions.MaxFields, Is.EqualTo(5));
            Assert.That(HostOptions.Failures(settings), Is.Empty);
        });
    }

    [Test]
    public void EmptySectionsAndValues_AreNoProblem()
    {
        Assert.That(
            HostOptions.Failures(new Dictionary<string, string?> { [$"{Root}:Limits"] = string.Empty, [$"{Root}:Designer"] = null, [$"{Root}:LockedSections"] = string.Empty }),
            Is.Empty);
    }

    [Test]
    public void ASiteWithAnInvalidConfiguration_DoesNotStart_AndSaysWhatToFix()
    {
        using var services = HostOptions.Services(new Dictionary<string, string?>
        {
            [$"{Limits}MaxFields"] = "0",
            [$"{Root}:LockedSection:0"] = "privacy",
        });

        Assert.That(
            () => HostOptions.Start(services),
            Throws.InstanceOf<OptionsValidationException>()
                .With.Message.Contains($"{Limits}MaxFields (0) must be 1 to 3000")
                .And.Message.Contains("JironFormBuilder:LockedSection is not a setting of Jiron Form Builder"));
    }

    [Test]
    public void AValueTheBinderCannotRead_StopsTheSite_NamingTheKey()
    {
        // The binder reads the values before the validator runs, and its message names the key too.
        using var services = HostOptions.Services(new Dictionary<string, string?> { [$"{Limits}MaxFields"] = "many" });

        Assert.That(() => HostOptions.Start(services), Throws.InvalidOperationException.With.Message.Contains($"'{Limits}MaxFields'"));
    }

    [Test]
    public void ASiteWithAValidConfiguration_Starts_WithTheLimitsItConfigured()
    {
        using var services = HostOptions.Services(new Dictionary<string, string?> { [$"{Limits}MaxFields"] = "12" });

        HostOptions.Start(services);

        Assert.Multiple(() =>
        {
            Assert.That(services.GetRequiredService<DefinitionLimits>().MaxFields, Is.EqualTo(12));
            Assert.That(services.GetRequiredService<DefinitionLimits>(), Is.SameAs(services.GetRequiredService<IOptions<FormBuilderOptions>>().Value.Limits.Definitions));
        });
    }

    [Test]
    public void ALoweredLimit_RefusesADefinitionTheDefaultAllows_AndAStoredOneStillReads()
    {
        using var services = HostOptions.Services(new Dictionary<string, string?> { [$"{Limits}MaxPages"] = "1" });
        HostOptions.Start(services);
        var lowered = new FormDefinitionSerializer(new FormDefinitionUpgrades([]), services.GetRequiredService<DefinitionLimits>());

        var byDefault = DefinitionFixtures.Serializer().Read(DefinitionFixtures.Full);
        var read = lowered.Read(DefinitionFixtures.Full);
        var stored = lowered.ReadStored(DefinitionFixtures.Full);

        Assert.Multiple(() =>
        {
            Assert.That(byDefault.Problems, Is.Empty, "the default allows the two pages");
            Assert.That(read.Problems.Select(p => (p.Code, p.Path)), Is.EqualTo(new[] { (DefinitionErrors.LimitExceeded, "$.pages") }));
            Assert.That(lowered.CheckSave(byDefault.Definition!).Select(p => (p.Code, p.Path)), Is.EqualTo(new[] { (DefinitionErrors.LimitExceeded, "$.pages") }));
            Assert.That(stored.Problems, Is.Empty, "a stored definition reads with the highest limits");
            Assert.That(FormDefinitionSerializer.WriteString(stored.Definition!), Is.EqualTo(FormDefinitionSerializer.WriteString(byDefault.Definition!)));
        });
    }

    [Test]
    public void AStoredDefinition_IsReadWithTheCeiling_NotWithoutLimits()
    {
        var tooLarge = new string(' ', DefinitionLimits.Ceiling.MaxBytes + 1);
        var atTheCeiling = new FormDefinitionSerializer(new FormDefinitionUpgrades([]), DefinitionLimits.Ceiling);

        Assert.Multiple(() =>
        {
            Assert.That(DefinitionFixtures.Serializer().ReadStored(tooLarge).Problems.Select(p => p.Code), Is.EqualTo(new[] { DefinitionErrors.LimitExceeded }));
            Assert.That(atTheCeiling.ReadStored(tooLarge).Problems.Select(p => p.Code), Is.EqualTo(new[] { DefinitionErrors.LimitExceeded }));
            Assert.That(atTheCeiling.ReadStored(DefinitionFixtures.Full).Problems, Is.Empty);
        });
    }

    private static IEnumerable<string> LimitNames() =>
        typeof(DefinitionLimits).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.PropertyType == typeof(int)).Select(p => p.Name);

    private static IEnumerable<string?> BadTypes() => [null, string.Empty, "1email", "e mail", "e/mail", "_email", "-email", ".email", "e" + (char)0xE9, new string('e', 65)];

    private static IEnumerable<string?> BadSettings() => [null, string.Empty, "From", "1from", "reply-to", "reply_to", "from.address", "f" + (char)0xF6, new string('f', 65)];

    private static int Limit(string name, DefinitionLimits limits) => (int)typeof(DefinitionLimits).GetProperty(name)!.GetValue(limits)!;

    private static IReadOnlyList<string> LimitFailures(string name, int value) =>
        HostOptions.Failures(new Dictionary<string, string?> { [Limits + name] = value.ToString(System.Globalization.CultureInfo.InvariantCulture) });

    private static Dictionary<string, string?> Lock(int index = 0, string? kind = "action", string? type = "email", string? setting = "from", string? value = null)
    {
        var settings = new Dictionary<string, string?>
        {
            [$"{Locks}{index}:Kind"] = kind,
            [$"{Locks}{index}:Type"] = type,
            [$"{Locks}{index}:Setting"] = setting,
        };
        if (value is not null)
        {
            settings[$"{Locks}{index}:Value"] = value;
        }

        return settings;
    }
}
