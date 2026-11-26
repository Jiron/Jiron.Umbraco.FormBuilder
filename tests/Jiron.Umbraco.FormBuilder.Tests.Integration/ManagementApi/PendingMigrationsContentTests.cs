using Jiron.Umbraco.FormBuilder.Core;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Testing.People;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Jiron.Umbraco.FormBuilder.Web.Picker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Infrastructure.Install;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// Umbraco's own changes while Jiron Form Builder's tables are not there yet (27.1, the first site's finding 3): a
/// document type changed, pages saved and deleted, and a user group deleted, as an install or an import does
/// before the package's migrations have run, write no warning and no stack trace, fail nothing, and leave one line at
/// Information; once the plan has run, the "used on" rebuild it asks for finds the page that picks a form.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class PendingMigrationsContentTests : FormBuilderTestServerTestBase
{
    private const string PageTypeAlias = "jfbPendingPage";

    /// <inheritdoc />
    protected override bool RunsTheMigrations => false;

    [Test]
    public async Task ChangesBeforeTheTablesAreThere_LogNoWarning_AndTheRebuildAfterThePlanFindsWhatTheyConcern()
    {
        var form = Guid.NewGuid();
        Assume.That(await GetRequiredService<IFormBuilderMigrationState>().HasTablesAsync(), Is.False);
        var logs = GetRequiredService<LogCapture>();
        var before = logs.Entries.Count;

        // As an install or an import does before the plan: a type with the picker, pages that pick a form, the type
        // changed, a page deleted, and a user group deleted.
        var contents = GetRequiredService<IContentService>();
        var types = GetRequiredService<IContentTypeService>();
        var type = await PageTypeAsync();
        var page = contents.Create("Picks a form", Constants.System.Root, PageTypeAlias);
        page.SetValue("form", form.ToString("D"));
        Assert.That(contents.Save(page).Success, Is.True);
        var gone = contents.Create("Gone", Constants.System.Root, PageTypeAlias);
        gone.SetValue("form", form.ToString("D"));
        Assert.That(contents.Save(gone).Success, Is.True);
        Assert.That(contents.Delete(gone).Success, Is.True);
        type.AddPropertyType(new PropertyType(GetRequiredService<IShortStringHelper>(), (await GetRequiredService<IDataTypeService>().GetAsync(Constants.DataTypes.Guids.TextstringGuid))!, "subtitle") { Name = "Subtitle" });
        Assert.That((await types.UpdateAsync(type, Constants.Security.SuperUserKey)).Success, Is.True);
        IUserGroup group;
        await using (var scope = Services.CreateAsyncScope())
        {
            group = await ActivatorUtilities.CreateInstance<TestPeople>(scope.ServiceProvider).EnsureUserGroupAsync(new TestUserGroup { Alias = "jfbPendingGroup", Name = "Pending group" });
        }

        var deleted = await GetRequiredService<IUserGroupService>().DeleteAsync(group.Key);
        var whilePending = logs.Entries.Skip(before).Where(entry => entry.Category.StartsWith("Jiron.Umbraco.FormBuilder", StringComparison.Ordinal)).ToList();

        // The plan runs; a form the page named comes with it, as a site's forms do when they come from files or a copy.
        var results = await GetRequiredService<PackageMigrationRunner>().RunPackagePlansAsync([FormBuilderConstants.MigrationPlanName]);
        Assert.That(results.Single().Successful, Is.True, results.Single().Exception?.ToString());
        GetRequiredService<IFormBuilderMigrationState>().Invalidate();
        var created = await GetRequiredService<IFormBuilderFormService>().CreateAsync(new NewFormRequest("Picked", Alias: "picked"));
        GiveKey(created.Key, form);
        var rebuild = GetRequiredService<FormReferenceRebuild>();
        var due = await rebuild.IsDueAsync();
        var done = await rebuild.ContinueAsync(DateTimeOffset.MaxValue);

        Assert.Multiple(async () =>
        {
            Assert.That(deleted.Success, Is.True, "Umbraco's own change fails nothing");
            Assert.That(whilePending.Where(entry => entry.Level >= LogLevel.Warning || entry.Exception is not null).Select(entry => $"{entry.Category}: {entry.Message}"), Is.Empty, "no warning and no stack trace");
            Assert.That(whilePending.Select(entry => (entry.Category, entry.Level)), Is.EqualTo(new[] { (typeof(FormBuilderTablesCheck).FullName!, LogLevel.Information) }), "one line, at Information");
            Assert.That((due, done), Is.EqualTo((true, true)), "the plan asked for the rebuild, which ran");
            Assert.That(await GetRequiredService<IReferenceRepository>().ContentOfAsync(form), Is.EqualTo(new[] { page.Key }), "what the changes concern is found");
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.Services.AddLogCapture();
    }

    // A page type with the form picker, as a site makes it.
    private async Task<IContentType> PageTypeAsync()
    {
        await GetRequiredService<FormBlockTypes>().CreateAsync(Constants.Security.SuperUserKey);
        var shortStrings = GetRequiredService<IShortStringHelper>();
        var type = new ContentType(shortStrings, -1) { Alias = PageTypeAlias, Name = "Pending page", AllowedAsRoot = true };
        type.AddPropertyType(new PropertyType(shortStrings, (await GetRequiredService<IDataTypeService>().GetAsync(FormBlockTypes.DataTypeKey))!, "form") { Name = "Form" });
        Assert.That((await GetRequiredService<IContentTypeService>().CreateAsync(type, Constants.Security.SuperUserKey)).Success, Is.True);
        return type;
    }

    // Gives a form the key a page named before the form was there.
    private void GiveKey(Guid key, Guid named)
    {
        using var scope = GetRequiredService<IScopeProvider>().CreateScope();
        var syntax = scope.Database.SqlContext.SqlSyntax;
        scope.Database.Execute($"UPDATE {syntax.GetQuotedTableName(FormBuilderTables.Form)} SET {syntax.GetQuotedColumnName("key")} = @0 WHERE {syntax.GetQuotedColumnName("key")} = @1", named, key);
        scope.Complete();
    }
}
