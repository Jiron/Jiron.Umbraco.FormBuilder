using System.Text.Json.Nodes;

namespace Jiron.Umbraco.FormBuilder.Core.Definitions.Upgrades;

/// <summary>
/// Upgrades a definition from one schema version to the next (ADR-0002): JSON in, JSON out, in memory. Stored versions
/// are never rewritten; reading one runs the upgraders from its version to the current one. An upgrader is tolerant:
/// it leaves what it does not recognise for the strict reader to refuse.
/// </summary>
internal interface IFormDefinitionUpgrader
{
    /// <summary>The version the upgrader reads; it writes the next one.</summary>
    int FromVersion { get; }

    /// <summary>Returns the document in the next version's shape; it may change and return <paramref name="definition"/>.</summary>
    JsonObject Upgrade(JsonObject definition);
}

/// <summary>
/// The chain of upgraders: exactly one per version from the oldest readable version up to the current one, checked
/// when the chain is built, so a missing step fails at start-up rather than when an old version is read.
/// </summary>
internal sealed class FormDefinitionUpgrades
{
    private readonly Dictionary<int, IFormDefinitionUpgrader> _byVersion;

    public FormDefinitionUpgrades(IEnumerable<IFormDefinitionUpgrader> upgraders)
        : this(upgraders, FormDefinition.CurrentSchemaVersion)
    {
    }

    /// <summary>A chain for <paramref name="currentVersion"/>; tests build chains for synthetic versions.</summary>
    /// <exception cref="InvalidOperationException">Two upgraders read one version, one reads the current version or later, or a step is missing.</exception>
    internal FormDefinitionUpgrades(IEnumerable<IFormDefinitionUpgrader> upgraders, int currentVersion)
    {
        ArgumentNullException.ThrowIfNull(upgraders);
        var map = new Dictionary<int, IFormDefinitionUpgrader>();
        foreach (var upgrader in upgraders)
        {
            if (upgrader.FromVersion >= currentVersion || upgrader.FromVersion < 0)
            {
                throw new InvalidOperationException($"{upgrader.GetType().Name} reads schema version {upgrader.FromVersion}, but the current version is {currentVersion}.");
            }

            if (!map.TryAdd(upgrader.FromVersion, upgrader))
            {
                throw new InvalidOperationException($"Two upgraders read schema version {upgrader.FromVersion}: {map[upgrader.FromVersion].GetType().Name} and {upgrader.GetType().Name}.");
            }
        }

        Current = currentVersion;
        Oldest = map.Count == 0 ? currentVersion : map.Keys.Min();
        for (var version = Oldest; version < currentVersion; version++)
        {
            if (!map.ContainsKey(version))
            {
                throw new InvalidOperationException($"No upgrader reads schema version {version}, so versions before it cannot reach {currentVersion}.");
            }
        }

        _byVersion = map;
    }

    /// <summary>The version documents are upgraded to.</summary>
    public int Current { get; }

    /// <summary>The oldest version that can be read.</summary>
    public int Oldest { get; }

    /// <summary>Upgrades a document of <paramref name="fromVersion"/> to <see cref="Current"/>, step by step.</summary>
    public JsonObject Upgrade(JsonObject definition, int fromVersion)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (fromVersion < Oldest || fromVersion > Current)
        {
            throw new ArgumentOutOfRangeException(nameof(fromVersion), fromVersion, $"Versions {Oldest} to {Current} can be read.");
        }

        for (var version = fromVersion; version < Current; version++)
        {
            definition = _byVersion[version].Upgrade(definition);
            definition["schemaVersion"] = version + 1;
        }

        return definition;
    }
}
