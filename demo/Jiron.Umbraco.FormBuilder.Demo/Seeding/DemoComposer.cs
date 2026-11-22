using Jiron.Umbraco.FormBuilder.Samples;
using Jiron.Umbraco.FormBuilder.Testing.Startup;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.Notifications;

namespace Jiron.Umbraco.FormBuilder.Demo.Seeding;

/// <summary>Registers the demo's seeding (ROADMAP 3.3), and the extension samples its settings name (ADR-0234).</summary>
public sealed class DemoComposer : IComposer
{
    /// <inheritdoc />
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.Configure<DemoOptions>(builder.Config.GetSection(DemoOptions.Section));
        builder.Services.AddTransient<DemoMemberSeeder>();
        builder.Services.AddTransient<DemoMediaSeeder>();
        builder.Services.AddTransient<DemoFormSeeder>();
        builder.AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, DemoSeeder>();

        // A test host waits for the seeding before Umbraco's start-up work (Testing's StartupSeeding).
        builder.Services.AddStartupSeeder(DemoSeeder.SeederName);
        foreach (var sample in builder.Config.GetSection($"{DemoOptions.Section}:{nameof(DemoOptions.Samples)}").Get<string[]>() ?? [])
        {
            builder.AddFormBuilderSample(sample);
        }
    }
}

/// <summary>
/// The demo's own settings (<c>Demo</c>): the generated members, which scripts/demo.ps1 passes through the
/// environment (<c>Demo__Members__0__Email</c> and so on) and never writes into a settings file.
/// </summary>
public sealed class DemoOptions
{
    /// <summary>The configuration section.</summary>
    public const string Section = "Demo";

    /// <summary>The library members to create when they are missing.</summary>
    public IList<DemoMemberOptions> Members { get; } = [];

    /// <summary>
    /// Whether the demo seeds its content (on by default): off, a fresh demo starts as a new site does, without pages, forms
    /// or the form block, as the setup checklist's run needs (ROADMAP 12.6).
    /// </summary>
    public bool Seed { get; set; } = true;

    /// <summary>
    /// The extension samples the demo turns on, by their names (<see cref="FormBuilderSamples.Names"/>): none by default,
    /// so the demo shows Jiron Form Builder as a site gets it; the browser tests name the ones they show.
    /// </summary>
    public IList<string> Samples { get; } = [];
}

/// <summary>A generated library member.</summary>
public sealed class DemoMemberOptions
{
    /// <summary>The member's name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The member's email address, also the user name.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>The member's password; never logged.</summary>
    public string Password { get; set; } = string.Empty;
}
