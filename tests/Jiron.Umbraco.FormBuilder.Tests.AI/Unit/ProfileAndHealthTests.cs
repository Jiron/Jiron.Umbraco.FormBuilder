using Jiron.Umbraco.FormBuilder.AI;
using Jiron.Umbraco.FormBuilder.AI.Health;
using Jiron.Umbraco.FormBuilder.AI.Profiles;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Testing.AI.FakeAI;
using Jiron.Umbraco.FormBuilder.Web.Setup;
using NSubstitute;
using Umbraco.AI.Core.Connections;
using Umbraco.AI.Core.Models;
using Umbraco.AI.Core.Profiles;

namespace Jiron.Umbraco.FormBuilder.Tests.AI.Unit;

/// <summary>
/// What the add-on says of its profile and its connection (ADR-0018), without a site, for the cases a test site cannot
/// make: a chosen profile that cannot chat, and a profile whose connection is gone, fails its test or throws there. Each
/// is said, and none falls back to another profile.
/// </summary>
[TestFixture]
public sealed class ProfileAndHealthTests
{
    private static readonly Guid Chosen = Guid.NewGuid();

    [Test]
    public async Task AChosenProfileThatCannotChat_IsSaid_AndRefused()
    {
        var choice = ChoiceOf(Profile(AICapability.Embedding, Guid.NewGuid()));

        var state = await choice.ResolveAsync();

        Assert.Multiple(() =>
        {
            Assert.That((state.ChosenId, state.Profile, state.Problem), Is.EqualTo(((Guid?)Chosen, (AIProfile?)null, "ai_profile_cannot_chat")));
            Assert.That(() => state.Usable(), Throws.TypeOf<AIUnavailableException>().With.Property(nameof(AIUnavailableException.Code)).EqualTo("ai_profile_cannot_chat"));
        });
    }

    [Test]
    public async Task AProfileWhoseConnectionIsGone_FailsItsTest_OrThrowsThere_IsSaid_InBothLanguages()
    {
        var connectionId = Guid.NewGuid();
        var choice = ChoiceOf(Profile(AICapability.Chat, connectionId));
        var connections = Substitute.For<IAIConnectionService>();
        var health = new AIHealth(choice, connections, TimeProvider.System);

        var gone = await health.FindAsync(HealthWords.Of("en"));
        connections.GetConnectionAsync(connectionId, Arg.Any<CancellationToken>()).Returns(new AIConnection { Alias = "c", Name = "Office AI", ProviderId = FormBuilderFakeProvider.ProviderId, IsActive = true });
        connections.TestConnectionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        var failed = await health.FindAsync(HealthWords.Of("de-DE"));
        var again = await health.FindAsync(HealthWords.Of("en"));

        Assert.Multiple(async () =>
        {
            Assert.That(gone.Select(finding => finding.Code), Is.EqualTo(new[] { "ai_connection_missing" }));
            Assert.That(gone.Single().Message, Does.Contain("Fake chat"), "the profile it belongs to");
            Assert.That(failed.Select(finding => finding.Code), Is.EqualTo(new[] { "ai_connection_failed" }));
            Assert.That(failed.Single().Message, Does.StartWith("Die KI-Verbindung").And.Contain("Office AI"), "in German");
            Assert.That(again.Select(finding => finding.Code), Is.EqualTo(new[] { "ai_connection_failed" }), "the test's answer stands");
            await connections.Received(1).TestConnectionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        });

        var throwing = Substitute.For<IAIConnectionService>();
        throwing.GetConnectionAsync(connectionId, Arg.Any<CancellationToken>()).Returns(new AIConnection { Alias = "c", Name = "Office AI", ProviderId = FormBuilderFakeProvider.ProviderId, IsActive = true });
        throwing.TestConnectionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns<bool>(_ => throw new InvalidOperationException("The provider is down."));
        var thrown = await new AIHealth(choice, throwing, TimeProvider.System).FindAsync(HealthWords.Of("en"));

        Assert.That(thrown.Select(finding => finding.Code), Is.EqualTo(new[] { "ai_connection_failed" }), "a test that throws fails");
    }

    private static AIProfile Profile(AICapability capability, Guid connectionId) => new()
    {
        Alias = "fake-chat",
        Name = "Fake chat",
        Capability = capability,
        Model = new AIModelRef(FormBuilderFakeProvider.ProviderId, FormBuilderFakeProvider.ChatModelId),
        ConnectionId = connectionId,
    };

    // The profile chosen in Settings > AI is the given one; nothing is written, so the change pipeline is not needed.
    private static AIProfileChoice ChoiceOf(AIProfile profile)
    {
        var keyValues = Substitute.For<IKeyValueRepository>();
        keyValues.GetAsync(FormBuilderAI.ProfileKey, Arg.Any<CancellationToken>()).Returns(Chosen.ToString("D"));
        var profiles = Substitute.For<IAIProfileService>();
        profiles.GetProfileAsync(Chosen, Arg.Any<CancellationToken>()).Returns(profile);
        return new AIProfileChoice(keyValues, profiles, null!);
    }
}
