using System.Net;
using Jiron.Umbraco.FormBuilder.Core.Composing;
using Jiron.Umbraco.FormBuilder.Core.Outbound;
using Jiron.Umbraco.FormBuilder.Testing.Actions;
using Jiron.Umbraco.FormBuilder.Testing.Csp;
using Jiron.Umbraco.FormBuilder.Testing.Endpoints;
using Jiron.Umbraco.FormBuilder.Testing.People;
using Jiron.Umbraco.FormBuilder.Testing.Startup;
using Jiron.Umbraco.FormBuilder.Testing.Stub;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Jiron.Umbraco.FormBuilder.Testing.Tls;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Extensions;

namespace Jiron.Umbraco.FormBuilder.Testing;

/// <summary>
/// Registers the test-only services in the hosts that reference this project (the test hosts, and the demo from 3.4c).
/// Everything is off unless the <c>JironFormBuilderTesting</c> section turns it on (<see cref="FormBuilderTestingOptions"/>):
/// the people seeder does nothing without people, the clock, the stub, HTTPS and the strict CSP are registered only when asked
/// for (the start-up marker too, in <see cref="Startup.StartupMarkerComposer"/>), and the test-only endpoints answer
/// only behind their gate (<see cref="TestEndpointsGate"/>). With the stub, calls to other hosts may reach it (ADR-0076).
/// </summary>
public sealed class FormBuilderTestingComposer : IComposer
{
    /// <inheritdoc />
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddOptions<FormBuilderTestingOptions>().Bind(builder.Config.GetSection(FormBuilderTestingOptions.SectionName));
        builder.Services.AddTransient<TestPeople>();
        builder.Services.AddTransient<TestAccess>();
        builder.AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, TestPeopleSeeder>();
        builder.Services.AddStartupSeeder(TestPeopleSeeder.SeederName);
        builder.Services.AddTransient<IStartupFilter, TestEndpointsStartupFilter>();

        var section = builder.Config.GetSection(FormBuilderTestingOptions.SectionName);
        if (section.GetValue<bool>(nameof(FormBuilderTestingOptions.Csp)))
        {
            builder.Services.AddTransient<IStartupFilter, TestCspStartupFilter>();
            builder.Services.AddSingleton<IFormBuilderCspNonceProvider, TestCspNonces>();
        }

        if (section.GetValue<string>(nameof(FormBuilderTestingOptions.HttpsKey)) is { Length: > 0 } httpsKey)
        {
            builder.Services.Configure<KestrelServerOptions>(kestrel =>
                kestrel.ConfigureHttpsDefaults(https => https.ServerCertificate = TestCertificate.Create(httpsKey, TimeProvider.System)));
        }

        if (section.GetValue<bool>(nameof(FormBuilderTestingOptions.StandInActionTypes)))
        {
            builder.JironFormBuilder().ActionTypes().Append<StandInEmailActionType>().Append<StandInWebhookActionType>();
        }

        if (!string.IsNullOrEmpty(section.GetValue<string>(nameof(FormBuilderTestingOptions.DeliveriesFile))))
        {
            builder.JironFormBuilder().ActionTypes().Append<RecordingActionType>();
        }

        if (section.GetValue<bool>(nameof(FormBuilderTestingOptions.Clock)))
        {
            var clock = new TestClock();
            builder.Services.AddSingleton(clock);
            builder.Services.AddUnique<TimeProvider>(clock);
        }

        if (section.GetValue<int>(nameof(FormBuilderTestingOptions.StubPort)) is var port and > 0)
        {
            builder.Services.AddSingleton<StubHostedService>();
            builder.Services.AddHostedService(services => services.GetRequiredService<StubHostedService>());

            // Calls to other hosts reach the stub, on loopback at its port only, in Development only, as the stub runs
            // there only; every other address is held to the site's rules (ADR-0076).
            builder.Services.AddUnique<IFormBuilderHttpTransport>(
                services => services.GetRequiredService<IHostEnvironment>().IsDevelopment()
                    ? new FormBuilderHttpTransport(null, new IPEndPoint(IPAddress.Loopback, port))
                    : new FormBuilderHttpTransport(),
                ServiceLifetime.Singleton);
        }
    }
}
