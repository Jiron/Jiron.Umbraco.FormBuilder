using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using NSubstitute;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Settings;

/// <summary>A settings service that answers the sections it is given, each with no overrides and no locks.</summary>
internal static class TestSettings
{
    /// <summary>A service with the files settings given, or their defaults.</summary>
    /// <param name="files">The files settings.</param>
    public static IFormBuilderSettingsService With(FilesSettings? files = null)
    {
        var settings = Substitute.For<IFormBuilderSettingsService>();
        settings.GetAsync<FilesSettings>(Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(Snapshot(files ?? new FilesSettings())));
        return settings;
    }

    /// <summary>A section's values as the service answers them.</summary>
    /// <param name="value">The values.</param>
    public static SettingsSnapshot<T> Snapshot<T>(T value)
        where T : ISettingsSection => new(value, 1, new HashSet<string>(), new Dictionary<string, SettingsLock>());
}
