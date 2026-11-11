using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Umbraco.AI.Core.EditableModels;
using Umbraco.AI.Core.Models;
using Umbraco.AI.Core.Providers;

namespace Jiron.Umbraco.FormBuilder.Testing.AI.FakeAI;

/// <summary>The fake provider's connection settings. It needs none; Umbraco.AI expects a settings type, and a connection's settings must not be null.</summary>
public sealed class FormBuilderFakeProviderSettings
{
    /// <summary>Not used.</summary>
    [AIField(Label = "Note", Description = "Not used. The fake provider needs no key or settings.")]
    public string? Note { get; set; }
}

/// <summary>
/// A test-only Umbraco.AI provider (D22, ADR-0018), ported from Tandem's: one scripted chat model, so every suite and the
/// demo run without a key. Umbraco.AI finds it by its attribute wherever this project is referenced: the add-on's tests
/// and the demo, never a package.
/// </summary>
[AIProvider(ProviderId, "Jiron Form Builder fake (tests and demo only)")]
public sealed class FormBuilderFakeProvider : AIProviderBase<FormBuilderFakeProviderSettings>
{
    /// <summary>The provider's id.</summary>
    public const string ProviderId = "jfb-fake";

    /// <summary>The scripted chat model.</summary>
    public const string ChatModelId = "fake-scripted";

    /// <summary>Creates the provider with its chat capability.</summary>
    public FormBuilderFakeProvider(IAIProviderInfrastructure infrastructure)
        : base(infrastructure) => WithCapability<FakeChatCapability>();
}

/// <summary>The fake provider's chat capability: one model, <see cref="FormBuilderFakeProvider.ChatModelId"/>.</summary>
public sealed class FakeChatCapability(FormBuilderFakeProvider provider, FakeChatJournal journal, ILoggerFactory loggerFactory)
    : AIChatCapabilityBase<FormBuilderFakeProviderSettings>(provider)
{
    /// <inheritdoc />
    protected override Task<IReadOnlyList<AIModelDescriptor>> GetModelsAsync(FormBuilderFakeProviderSettings settings, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AIModelDescriptor>>(
            [new AIModelDescriptor(new AIModelRef(FormBuilderFakeProvider.ProviderId, FormBuilderFakeProvider.ChatModelId), "Fake scripted model")]);

    /// <inheritdoc />
    protected override IChatClient CreateClient(FormBuilderFakeProviderSettings settings, string? modelId) =>
        new ScriptedChatClient(journal, loggerFactory.CreateLogger<ScriptedChatClient>());
}

/// <summary>
/// What the fake model received and did, for tests to read: each request's roles and schema, and every message's whole
/// text, which a test searches for what must never reach an AI. A singleton per host; it keeps the last
/// <see cref="Capacity"/> entries.
/// </summary>
public sealed class FakeChatJournal
{
    /// <summary>How many entries are kept.</summary>
    public const int Capacity = 500;

    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _entries = new();

    /// <summary>The entries, oldest first.</summary>
    public IReadOnlyList<string> Entries => [.. _entries];

    /// <summary>Adds an entry and drops the oldest beyond <see cref="Capacity"/>.</summary>
    public void Record(string entry)
    {
        _entries.Enqueue(entry);
        while (_entries.Count > Capacity && _entries.TryDequeue(out _))
        {
        }
    }

    /// <summary>Forgets every entry.</summary>
    public void Clear() => _entries.Clear();
}
