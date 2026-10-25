using Jiron.Umbraco.FormBuilder.Core;
using Jiron.Umbraco.FormBuilder.Core.Audit;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Scoping;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration;

/// <summary>
/// The steps every change of Core's services takes (ADR-0053), on a real site: refused inside a unit of work or a scope,
/// the cancelable notification, the write with its audit entries in one unit, and the notification afterwards, whose
/// failing handler is logged as the service's and leaves the change standing.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class FormBuilderChangesTests : RepositoryTestBase
{
    private const string Refusal = "JFB_TEST: a change of its own";

    private FormBuilderChanges<FormBuilderChangesTests> Changes => GetRequiredService<FormBuilderChanges<FormBuilderChangesTests>>();

    private LogCapture Logs => GetRequiredService<LogCapture>();

    [Test]
    public void AChange_InsideAUnitOfWorkOrAScope_IsRefusedWithItsReason_AndOutsideBothGoesOn()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => Database.WriteAsync(_ => { Changes.EnsureOwnUnit(Refusal); return Task.FromResult(true); }), Throws.InvalidOperationException.With.Message.EqualTo(Refusal));
            using (GetRequiredService<ICoreScopeProvider>().CreateCoreScope())
            {
                Assert.That(() => Changes.EnsureOwnUnit(Refusal), Throws.InvalidOperationException.With.Message.EqualTo(Refusal), "in a scope");
            }

            Assert.That(() => Changes.EnsureOwnUnit(Refusal), Throws.Nothing);
        });
    }

    [Test]
    public async Task TheWrite_AndItsAuditEntries_CommitTogether()
    {
        var key = Guid.NewGuid();

        var written = await Changes.WriteAsync(
            async () =>
            {
                await KeyValues.SetAsync("jfbtest.written", "yes");
                return "yes";
            },
            value => [Entry("test.first", key, value), Entry("test.second", key, value)],
            CancellationToken.None);
        var nothing = await Changes.WriteAsync(() => Task.FromResult<string?>(null), value => value is null ? [] : [Entry("test.none", key, value)], CancellationToken.None);

        Assert.Multiple(async () =>
        {
            Assert.That((written, nothing), Is.EqualTo(("yes", (string?)null)));
            Assert.That(await KeyValues.GetAsync("jfbtest.written"), Is.EqualTo("yes"));
            Assert.That(await ActionsAsync(key), Is.EqualTo(new[] { "test.first", "test.second" }), "in the order given; none for a write that found nothing");
        });
    }

    [Test]
    public async Task AWriteWhoseAuditEntryCannotBeMade_LeavesNothing()
    {
        var key = Guid.NewGuid();

        Assert.That(
            async () => await Changes.WriteAsync(
                async () =>
                {
                    await KeyValues.SetAsync("jfbtest.rolledback", "yes");
                    return 1;
                },
                _ => throw new InvalidOperationException("JFB_TEST: no entry"),
                CancellationToken.None),
            Throws.InvalidOperationException.With.Message.EqualTo("JFB_TEST: no entry"));

        Assert.Multiple(async () =>
        {
            Assert.That(await KeyValues.GetAsync("jfbtest.rolledback"), Is.Null, "the change went with its entry");
            Assert.That(await ActionsAsync(key), Is.Empty);
        });
    }

    [Test]
    public void ACancelledNotification_StopsTheChange_WithTheHandlersMessages()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                () => Changes.CancelableAsync(new TestChangingNotification(refuse: true)),
                Throws.TypeOf<FormBuilderCancelledException>().With.Property(nameof(FormBuilderCancelledException.Messages)).EqualTo(new[] { new CancelMessage("JFB_TEST", "not now") }));
            Assert.That(() => Changes.CancelableAsync(new TestChangingNotification(refuse: false)), Throws.Nothing);
        });
    }

    [Test]
    public async Task AFailingHandlerAfterTheCommit_IsLoggedAsTheServices_WithWhatChanged_AndTheChangeStands()
    {
        await Changes.PublishedAsync(new TestChangedNotification(), "the test subject");

        var errors = Logs.Entries.Where(entry => entry.Category == typeof(FormBuilderChangesTests).FullName && entry.Level == LogLevel.Error).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(errors.Select(entry => entry.Message), Is.EqualTo(new[] { "A handler of TestChangedNotification failed after a change to the test subject committed; the change stands" }));
            Assert.That(errors.Single().Exception?.Message, Is.EqualTo("JFB_TEST: a handler that fails"));
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        base.CustomTestSetup(builder);
        builder.Services.AddLogCapture();
        builder.AddNotificationAsyncHandler<TestChangingNotification, TestChangeHandler>();
        builder.AddNotificationAsyncHandler<TestChangedNotification, TestChangeHandler>();
    }

    private static NewAuditEntry Entry(string action, Guid key, string value) =>
        new(action, "test", key, Editor, new AuditDetails().AddIdentifier("value", value));

    private async Task<string[]> ActionsAsync(Guid key) =>
        [.. (await Audit.QueryAsync(new AuditQuery { EntityKey = key }, PageRequest.First(10))).Items.Select(entry => entry.Action).Reverse()];

    /// <summary>A notification before a test change, which its handler cancels when asked to.</summary>
    public sealed class TestChangingNotification(bool refuse) : CancelableNotification(new EventMessages())
    {
        /// <summary>Whether the handler cancels the change.</summary>
        public bool Refuse { get; } = refuse;
    }

    /// <summary>A notification after a test change, whose handler fails.</summary>
    public sealed class TestChangedNotification : INotification;

    /// <summary>Cancels a test change when asked to, and fails after one.</summary>
    public sealed class TestChangeHandler : INotificationAsyncHandler<TestChangingNotification>, INotificationAsyncHandler<TestChangedNotification>
    {
        /// <inheritdoc />
        public Task HandleAsync(TestChangingNotification notification, CancellationToken cancellationToken)
        {
            if (notification.Refuse)
            {
                notification.CancelOperation(new EventMessage("JFB_TEST", "not now", EventMessageType.Error));
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task HandleAsync(TestChangedNotification notification, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("JFB_TEST: a handler that fails");
    }
}
