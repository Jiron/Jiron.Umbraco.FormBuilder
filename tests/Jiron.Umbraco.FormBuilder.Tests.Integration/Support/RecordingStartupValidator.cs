using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Support;

/// <summary>
/// The site's start-up validator, which keeps what it refused instead of throwing it: the test harness starts the site
/// before any test runs, so a test of a start that is refused reads the refusal here.
/// </summary>
/// <param name="site">The site's own start-up validator.</param>
internal sealed class RecordingStartupValidator(IStartupValidator site) : IStartupValidator
{
    /// <summary>How often the host ran it.</summary>
    public int Runs { get; private set; }

    /// <summary>What the site's validator refused, or null.</summary>
    public OptionsValidationException? Refusal { get; private set; }

    /// <summary>Puts the recording validator in the place of the site's own, which the composers registered before.</summary>
    /// <param name="services">The site's services.</param>
    public static void Wrap(IServiceCollection services)
    {
        var site = services.Last(service => service.ServiceType == typeof(IStartupValidator));
        var type = site.ImplementationType ?? throw new InvalidOperationException("The site's start-up validator is not registered by its type.");
        services.Remove(site);
        services.AddSingleton<IStartupValidator>(provider => new RecordingStartupValidator((IStartupValidator)ActivatorUtilities.CreateInstance(provider, type)));
    }

    /// <inheritdoc />
    public void Validate()
    {
        Runs++;
        try
        {
            site.Validate();
        }
        catch (OptionsValidationException exception)
        {
            Refusal = exception;
        }
    }
}
