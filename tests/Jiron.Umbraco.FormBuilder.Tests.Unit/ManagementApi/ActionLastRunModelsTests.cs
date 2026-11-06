using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Designer;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.ManagementApi;

/// <summary>An action's last run as the management API says it (ADR-0195): its times, and a next try only while it waits for one.</summary>
[TestFixture]
public sealed class ActionLastRunModelsTests
{
    private static readonly DateTimeOffset First = new(2026, 11, 6, 13, 40, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Latest = First.AddMinutes(22);
    private static readonly DateTimeOffset Due = Latest.AddMinutes(28);

    private static StoredActionRun Run(ActionRunState state) =>
        new(7, 3, 2, Guid.NewGuid(), "webhook", ActionTrigger.Submitted, 0, false, state, 2, Due, Guid.NewGuid(), null, null, new DeliveryError("http_503", "Service Unavailable"), First.AddMinutes(-1), First, null)
        {
            LastAttemptUtc = Latest,
        };

    [Test]
    public void ARunThatWaits_SaysItsNextTry_AndItsFirstAndLatestAttempt()
    {
        var waiting = ActionLastRunResponseModel.Of(Run(ActionRunState.Pending));

        Assert.That(
            (waiting.State, waiting.Attempts, waiting.FirstAttemptUtc, waiting.LastAttemptUtc, waiting.NextAttemptUtc, waiting.ErrorCode, waiting.ErrorMessage),
            Is.EqualTo(("pending", 2, (DateTimeOffset?)First, (DateTimeOffset?)Latest, (DateTimeOffset?)Due, "http_503", "Service Unavailable")));
    }

    [Test]
    public void ARunTheQueueDoesNotTryAgain_SaysNoNextTry_ThoughItKeepsTheTimeItWasDueAt() =>
        Assert.Multiple(() =>
        {
            foreach (var state in new[] { ActionRunState.Running, ActionRunState.Uncertain, ActionRunState.Failed, ActionRunState.Succeeded, ActionRunState.Skipped })
            {
                Assert.That(ActionLastRunResponseModel.Of(Run(state)).NextAttemptUtc, Is.Null, StoredNames.Of(state));
            }
        });
}
