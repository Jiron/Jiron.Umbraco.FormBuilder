using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.Notifications;

namespace Jiron.Umbraco.FormBuilder.Demo.Seeding;

/// <summary>Registers the demo's seeding (ROADMAP 3.3).</summary>
public sealed class DemoComposer : IComposer
{
    /// <inheritdoc />
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.Configure<DemoOptions>(builder.Config.GetSection(DemoOptions.Section));
        builder.Services.AddTransient<DemoMemberSeeder>();
        builder.AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, DemoSeeder>();
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
