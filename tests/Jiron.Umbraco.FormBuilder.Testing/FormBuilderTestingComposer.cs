using Jiron.Umbraco.FormBuilder.Testing.Endpoints;
using Jiron.Umbraco.FormBuilder.Testing.People;
using Jiron.Umbraco.FormBuilder.Testing.Stub;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Extensions;

namespace Jiron.Umbraco.FormBuilder.Testing;

/// <summary>
/// Registers the test-only services in the hosts that reference this project (the test hosts, and the demo from 3.4c).
/// Everything is off unless the <c>JironFormBuilderTesting</c> section turns it on (<see cref="FormBuilderTestingOptions"/>):
/// the people seeder does nothing without people, the clock and the stub are registered only when asked for (the
/// start-up marker too, in <see cref="Startup.StartupMarkerComposer"/>), and the test-only endpoints answer only behind
/// their gate (<see cref="TestEndpointsGate"/>).
/// </summary>
public sealed class FormBuilderTestingComposer : IComposer
{
    /// <inheritdoc />
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddOptions<FormBuilderTestingOptions>().Bind(builder.Config.GetSection(FormBuilderTestingOptions.SectionName));
        builder.Services.AddTransient<TestPeople>();
        builder.AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, TestPeopleSeeder>();
        builder.Services.AddTransient<IStartupFilter, TestEndpointsStartupFilter>();

        var section = builder.Config.GetSection(FormBuilderTestingOptions.SectionName);
        if (section.GetValue<bool>(nameof(FormBuilderTestingOptions.Clock)))
        {
            var clock = new TestClock();
            builder.Services.AddSingleton(clock);
            builder.Services.AddUnique<TimeProvider>(clock);
        }

        if (section.GetValue<int>(nameof(FormBuilderTestingOptions.StubPort)) > 0)
        {
            builder.Services.AddSingleton<StubHostedService>();
            builder.Services.AddHostedService(services => services.GetRequiredService<StubHostedService>());
        }
    }
}
