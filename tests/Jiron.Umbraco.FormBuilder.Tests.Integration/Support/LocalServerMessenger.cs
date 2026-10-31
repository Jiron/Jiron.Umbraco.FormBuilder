using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Sync;
using Umbraco.Cms.Infrastructure.Sync;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Support;

/// <summary>
/// A server messenger that delivers each cache message on this server at once, as a site of one server does, where the
/// test host's own messenger delivers none: a test that needs a cache refresher to run (and its notification to be
/// published) registers it with <c>AddUnique&lt;IServerMessenger, LocalServerMessenger&gt;()</c>.
/// </summary>
/// <param name="serializer">Umbraco's JSON serializer.</param>
internal sealed class LocalServerMessenger(IJsonSerializer serializer) : ServerMessengerBase(distributedEnabled: false, serializer)
{
    /// <inheritdoc />
    public override void Sync()
    {
    }

    /// <inheritdoc />
    public override void SendMessages()
    {
    }

    /// <inheritdoc />
    protected override void DeliverRemote(ICacheRefresher refresher, MessageType messageType, IEnumerable<object>? ids = null, string? json = null)
    {
    }
}
