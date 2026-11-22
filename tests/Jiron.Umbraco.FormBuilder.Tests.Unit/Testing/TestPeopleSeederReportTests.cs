using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Testing.People;
using Jiron.Umbraco.FormBuilder.Testing.Startup;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Testing;

/// <summary>
/// The test people's seeder reports its end to the host's start-up seeding whatever happened (ROADMAP 24.1), so
/// Umbraco's start-up work, which waits for the report, is never left waiting.
/// </summary>
[TestFixture]
public sealed class TestPeopleSeederReportTests
{
    [Test]
    public async Task HandleAsync_NothingToSeed_Reports()
    {
        var seeding = Seeding();

        await Seeder(new FormBuilderTestingOptions(), RuntimeLevel.Run, Substitute.For<IUserGroupService>(), seeding).HandleAsync(Started(), CancellationToken.None);

        Assert.That(seeding.IsFinished, Is.True);
    }

    [Test]
    public async Task HandleAsync_ASiteThatDoesNotRun_Reports()
    {
        var seeding = Seeding();

        await Seeder(WithAGroup(), RuntimeLevel.Install, Substitute.For<IUserGroupService>(), seeding).HandleAsync(Started(), CancellationToken.None);

        Assert.That(seeding.IsFinished, Is.True);
    }

    [Test]
    public void HandleAsync_TheSeedingFails_ReportsAndStillFails()
    {
        var seeding = Seeding();
        var groups = Substitute.For<IUserGroupService>();
        groups.GetAsync(Arg.Any<string>()).ThrowsAsync(new InvalidOperationException("no database"));

        Assert.ThrowsAsync<InvalidOperationException>(() => Seeder(WithAGroup(), RuntimeLevel.Run, groups, seeding).HandleAsync(Started(), CancellationToken.None));

        Assert.That(seeding.IsFinished, Is.True);
    }

    private static FormBuilderTestingOptions WithAGroup()
    {
        var options = new FormBuilderTestingOptions();
        options.UserGroups.Add(new TestUserGroup { Alias = "jfbEditors" });
        return options;
    }

    private static StartupSeeding Seeding() =>
        new([new StartupSeeder(TestPeopleSeeder.SeederName)], NullLogger<StartupSeeding>.Instance);

    private static TestPeopleSeeder Seeder(FormBuilderTestingOptions settings, RuntimeLevel level, IUserGroupService groups, StartupSeeding seeding)
    {
        var runtime = Substitute.For<IRuntimeState>();
        runtime.Level.Returns(level);
        var people = new TestPeople(
            Substitute.For<IUserService>(),
            groups,
            Substitute.For<IShortStringHelper>(),
            Substitute.For<IMemberService>(),
            Substitute.For<IMemberGroupService>(),
            Substitute.For<IMemberTypeService>(),
            Substitute.For<IMemberEditingService>());
        var access = new TestAccess(Substitute.For<IFormBuilderAccessService>(), Substitute.For<IFormBuilderFolderService>(), Substitute.For<IPermissionRepository>(), groups);
        return new TestPeopleSeeder(Options.Create(settings), runtime, people, access, seeding, NullLogger<TestPeopleSeeder>.Instance);
    }

    private static UmbracoApplicationStartedNotification Started() => new(isRestarting: false);
}
