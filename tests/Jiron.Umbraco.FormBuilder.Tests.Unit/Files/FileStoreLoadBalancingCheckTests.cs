using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Sync;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Files;

/// <summary>
/// A load-balanced site that keeps uploads in the default folder, on each server's own disk, is told so at start-up
/// (ADR-0041, amended in 4.8c); a single server, a root the host set and a store of the site's own are not.
/// </summary>
[TestFixture]
public sealed class FileStoreLoadBalancingCheckTests
{
    // Only names: nothing is created.
    private static readonly string Site = Path.Combine(Path.GetTempPath(), "jfb-site");

    [TestCase(ServerRole.Subscriber, 1)]
    [TestCase(ServerRole.Subscriber, 0)]
    [TestCase(ServerRole.SchedulingPublisher, 2)]
    [TestCase(ServerRole.Unknown, 3)]
    public void ALoadBalancedServer_WithTheDefaultFolder_LogsAnErrorNamingTheSetting(ServerRole role, int active)
    {
        var log = Check(role, active);

        Assert.Multiple(() =>
        {
            Assert.That(log.Entries.Select(entry => entry.Level), Is.EqualTo(new[] { LogLevel.Error }));
            Assert.That(log.Entries.Single().Message, Does.Contain("JironFormBuilder:Files:Root").And.Contain(FormBuilderFilesOptions.DefaultRoot));
            Assert.That(FormBuilderFilesOptions.RootKey, Is.EqualTo("JironFormBuilder:Files:Root"));
        });
    }

    [TestCase(ServerRole.Single, 3)]
    [TestCase(ServerRole.SchedulingPublisher, 1)]
    [TestCase(ServerRole.Unknown, 1)]
    [TestCase(ServerRole.Unknown, 0)]
    public void ASingleServer_LogsNothing_AlsoWhenTheElectionMadeItThePublisher(ServerRole role, int active)
    {
        Assert.That(Check(role, active).Entries, Is.Empty);
    }

    [Test]
    public void ASingleServer_NeverReadsTheRegistrations()
    {
        var registrations = Substitute.For<IServerRegistrationService>();

        Check(ServerRole.Single, 3, registrations: registrations);

        registrations.DidNotReceiveWithAnyArgs().GetActiveServers();
    }

    [TestCase("D:/shared/uploads")]
    [TestCase("~/App_Data/uploads")]
    public void ARootTheHostSet_LogsNothing(string root)
    {
        Assert.That(Check(ServerRole.Subscriber, 2, root).Entries, Is.Empty);
    }

    [TestCase("~/umbraco/data/jironformbuilder/uploads")]
    [TestCase("~/umbraco/Data/JironFormBuilder/Uploads/")]
    [TestCase(" ~/umbraco/Data/JironFormBuilder/Uploads ")]
    public void TheDefaultWrittenAnotherWay_IsTheDefault(string root)
    {
        Assert.That(Check(ServerRole.Subscriber, 2, root).Entries.Select(entry => entry.Level), Is.EqualTo(new[] { LogLevel.Error }));
    }

    [Test]
    public void AStoreOfTheSitesOwn_OrAnUmbracoThatDoesNotRun_LogsNothing()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Check(ServerRole.Subscriber, 2, store: Substitute.For<IFormBuilderFileStore>()).Entries, Is.Empty);
            Assert.That(Check(ServerRole.Subscriber, 2, level: RuntimeLevel.Upgrade).Entries, Is.Empty);
        });
    }

    [Test]
    public void RegistrationsThatCannotBeRead_AreAWarning_AndTheStartGoesOn()
    {
        var registrations = Substitute.For<IServerRegistrationService>();
        registrations.GetActiveServers(Arg.Any<bool>()).Throws(new InvalidOperationException("JFB_TEST: no database"));

        var log = Check(ServerRole.Unknown, 0, registrations: registrations);

        Assert.That(log.Entries.Select(entry => (entry.Level, entry.Message.Contains("InvalidOperationException", StringComparison.Ordinal))), Is.EqualTo(new[] { (LogLevel.Warning, true) }));
    }

    private static RecordingLogger Check(
        ServerRole role,
        int active,
        string root = FormBuilderFilesOptions.DefaultRoot,
        IFormBuilderFileStore? store = null,
        RuntimeLevel level = RuntimeLevel.Run,
        IServerRegistrationService? registrations = null)
    {
        var options = new FormBuilderOptions();
        options.Files.Root = root;
        var folders = Substitute.For<IHostFolders>();
        folders.ContentRoot.Returns(Site);
        folders.WebRoot.Returns(Path.Combine(Site, "wwwroot"));
        var roles = Substitute.For<IServerRoleAccessor>();
        roles.CurrentServerRole.Returns(role);
        if (registrations is null)
        {
            registrations = Substitute.For<IServerRegistrationService>();
            registrations.GetActiveServers(true).Returns([.. Enumerable.Range(0, active).Select(_ => Substitute.For<IServerRegistration>())]);
        }

        var runtime = Substitute.For<IRuntimeState>();
        runtime.Level.Returns(level);
        var log = new RecordingLogger();
        store ??= new PhysicalFileStore(Options.Create(new FormBuilderOptions()), folders, NullLogger<PhysicalFileStore>.Instance);
        new FileStoreLoadBalancingCheck(Options.Create(options), store, roles, registrations, runtime, log).Handle(new UmbracoApplicationStartedNotification(false));
        return log;
    }

    private sealed class RecordingLogger : ILogger<FileStoreLoadBalancingCheck>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
