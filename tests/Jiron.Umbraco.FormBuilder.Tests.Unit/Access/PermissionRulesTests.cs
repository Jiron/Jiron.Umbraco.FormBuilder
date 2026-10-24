using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Access;

/// <summary>The permissions as flags, with their names, values, dependencies and where they are granted (ADR-0013, ADR-0048).</summary>
[TestFixture]
public sealed class PermissionRulesTests
{
    // ADR-0048's table: the values are stored and never change.
    private static readonly (FormBuilderPermissions Permission, string Name, int Value)[] Table =
    [
        (FormBuilderPermissions.Design, "design", 1),
        (FormBuilderPermissions.ManageActions, "manageActions", 2),
        (FormBuilderPermissions.ViewEntries, "viewEntries", 4),
        (FormBuilderPermissions.ViewSensitive, "viewSensitive", 8),
        (FormBuilderPermissions.EditEntries, "editEntries", 16),
        (FormBuilderPermissions.DeleteEntries, "deleteEntries", 32),
        (FormBuilderPermissions.Export, "export", 64),
        (FormBuilderPermissions.ManageLibrary, "manageLibrary", 128),
        (FormBuilderPermissions.ManageSettings, "manageSettings", 256),
        (FormBuilderPermissions.ManagePermissions, "managePermissions", 512),
        (FormBuilderPermissions.PrivacyTools, "privacyTools", 1024),
    ];

    [Test]
    public void EachPermission_KeepsItsNameAndItsStoredValue()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PermissionRules.Each, Is.EqualTo(Table.Select(row => row.Permission)), "in the order of ADR-0013's table");
            Assert.That(Enum.GetValues<FormBuilderPermissions>().Where(permission => permission != FormBuilderPermissions.None), Is.EquivalentTo(Table.Select(row => row.Permission)), "no permission without a row");
            foreach (var (permission, name, value) in Table)
            {
                Assert.That((int)permission, Is.EqualTo(value), name);
                Assert.That(PermissionRules.NameOf(permission), Is.EqualTo(name));
            }
        });
    }

    [Test]
    public void TheFolderLevelAndTheGlobalOnlyPermissions_SplitEveryPermission()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PermissionRules.NamesOf(PermissionRules.FolderLevel), Is.EqualTo(new[] { "design", "manageActions", "viewEntries", "viewSensitive", "editEntries", "deleteEntries", "export" }));
            Assert.That(PermissionRules.NamesOf(PermissionRules.GlobalOnly), Is.EqualTo(new[] { "manageLibrary", "manageSettings", "managePermissions", "privacyTools" }));
            Assert.That(PermissionRules.FolderLevel & PermissionRules.GlobalOnly, Is.EqualTo(FormBuilderPermissions.None));
            Assert.That((int)PermissionRules.All, Is.EqualTo(2047));
        });
    }

    [Test]
    public void Names_AreWrittenInTheTablesOrder_AndReadInAnyCase()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PermissionRules.NamesOf(FormBuilderPermissions.Export | FormBuilderPermissions.Design), Is.EqualTo(new[] { "design", "export" }));
            Assert.That(PermissionRules.NamesOf(FormBuilderPermissions.None), Is.Empty);
            Assert.That(PermissionRules.TryParse("VIEWENTRIES", out var upper) && upper == FormBuilderPermissions.ViewEntries, Is.True);
            Assert.That(PermissionRules.TryParse("privacytools", out var lower) && lower == FormBuilderPermissions.PrivacyTools, Is.True);
            Assert.That(PermissionRules.TryParse("viewEntry", out var unknown), Is.False);
            Assert.That(unknown, Is.EqualTo(FormBuilderPermissions.None));
            Assert.That(PermissionRules.TryParse(null, out _), Is.False);
            Assert.That(() => PermissionRules.NameOf(FormBuilderPermissions.Design | FormBuilderPermissions.Export), Throws.InstanceOf<ArgumentOutOfRangeException>(), "a name is one permission's");
        });
    }

    [Test]
    public void AStoredValueThisVersionDoesNotKnow_GrantsNothing() =>
        Assert.That(PermissionRules.FromStored(4096 | 4 | 1), Is.EqualTo(FormBuilderPermissions.ViewEntries | FormBuilderPermissions.Design));

    [Test]
    public void EachPermission_NamesTheOneItNeeds()
    {
        Assert.Multiple(() =>
        {
            foreach (var permission in new[] { FormBuilderPermissions.ViewSensitive, FormBuilderPermissions.EditEntries, FormBuilderPermissions.DeleteEntries, FormBuilderPermissions.Export })
            {
                Assert.That(PermissionRules.Needs(permission), Is.EqualTo(FormBuilderPermissions.ViewEntries), PermissionRules.NameOf(permission));
            }

            Assert.That(PermissionRules.Needs(FormBuilderPermissions.ManageActions), Is.EqualTo(FormBuilderPermissions.Design));
            foreach (var permission in PermissionRules.Each.Where(each => (each & (PermissionRules.GlobalOnly | FormBuilderPermissions.Design | FormBuilderPermissions.ViewEntries)) != 0))
            {
                Assert.That(PermissionRules.Needs(permission), Is.EqualTo(FormBuilderPermissions.None), PermissionRules.NameOf(permission));
            }
        });
    }

    [Test]
    public void ASetWithoutADependency_NamesEachPermissionThatLacksIt()
    {
        var missing = PermissionRules.MissingDependencies(FormBuilderPermissions.Export | FormBuilderPermissions.ManageActions | FormBuilderPermissions.ViewSensitive | FormBuilderPermissions.ManageSettings);

        Assert.Multiple(() =>
        {
            Assert.That(missing, Is.EqualTo(new[]
            {
                new MissingDependency(FormBuilderPermissions.ManageActions, FormBuilderPermissions.Design),
                new MissingDependency(FormBuilderPermissions.ViewSensitive, FormBuilderPermissions.ViewEntries),
                new MissingDependency(FormBuilderPermissions.Export, FormBuilderPermissions.ViewEntries),
            }));
            Assert.That(PermissionRules.MissingDependencies(PermissionRules.All), Is.Empty);
            Assert.That(PermissionRules.MissingDependencies(FormBuilderPermissions.Export | FormBuilderPermissions.ViewEntries), Is.Empty);
            Assert.That(PermissionRules.MissingDependencies(FormBuilderPermissions.None), Is.Empty);
        });
    }

    [Test]
    public void OnlyAGlobalGrant_TakesTheGlobalOnlyPermissions()
    {
        var mixed = FormBuilderPermissions.Design | FormBuilderPermissions.ManageLibrary | FormBuilderPermissions.PrivacyTools;

        Assert.Multiple(() =>
        {
            Assert.That(PermissionRules.NotGrantable(PermissionTarget.Global, PermissionRules.All), Is.EqualTo(FormBuilderPermissions.None));
            Assert.That(PermissionRules.NotGrantable(PermissionTarget.Folder, mixed), Is.EqualTo(FormBuilderPermissions.ManageLibrary | FormBuilderPermissions.PrivacyTools));
            Assert.That(PermissionRules.NotGrantable(PermissionTarget.Form, mixed), Is.EqualTo(FormBuilderPermissions.ManageLibrary | FormBuilderPermissions.PrivacyTools));
            Assert.That(PermissionRules.NotGrantable(PermissionTarget.Form, PermissionRules.FolderLevel), Is.EqualTo(FormBuilderPermissions.None));
        });
    }
}
