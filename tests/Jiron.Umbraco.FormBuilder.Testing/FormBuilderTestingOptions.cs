using Jiron.Umbraco.FormBuilder.Testing.People;

namespace Jiron.Umbraco.FormBuilder.Testing;

/// <summary>
/// What a test host turns on (the <c>JironFormBuilderTesting</c> section). Everything is off by default, so the demo and
/// any other host that references this project behave as a site does. The browser tests' global set-up passes these as
/// environment variables (<c>JironFormBuilderTesting__Endpoints=true</c> and so on); passwords never sit in a file
/// inside the repository.
/// </summary>
public sealed class FormBuilderTestingOptions
{
    /// <summary>The configuration section the options bind to.</summary>
    public const string SectionName = "JironFormBuilderTesting";

    /// <summary>
    /// Registers stand-ins under the aliases of the action types later steps bring, for hosts that test what forms do
    /// with actions.
    /// </summary>
    public bool StandInActionTypes { get; set; }

    /// <summary>A file every host of a test appends its deliveries to, which registers the action type that does so (ADR-0158).</summary>
    public string? DeliveriesFile { get; set; }

    /// <summary>
    /// True to answer the test-only paths under <c>/jfb-testing/</c>. Even then they answer only in the Development
    /// environment and only requests from this machine (<see cref="Endpoints.TestEndpointsGate"/>).
    /// </summary>
    public bool Endpoints { get; set; }

    /// <summary>True to run the host on <see cref="Time.TestClock"/>, which the test-only endpoints move.</summary>
    public bool Clock { get; set; }

    /// <summary>
    /// The port of the HTTP stub this host starts, for example 5560 (ROADMAP section 4.11); 0 starts none. Only a host in
    /// the Development environment starts it.
    /// </summary>
    public int StubPort { get; set; }

    /// <summary>
    /// True to log <see cref="Startup.StartupMarkerPopulator.Marker"/> whenever Umbraco has rebuilt its indexes, which
    /// the browser tests' start-up wait looks for.
    /// </summary>
    public bool StartupMarker { get; set; }

    /// <summary>
    /// True to give every response the strict Content Security Policy of <see cref="Csp.TestCspMiddleware"/>, with a nonce
    /// for the requests that ask for one (ROADMAP 6.4k); only a host in the Development environment sets it.
    /// </summary>
    public bool Csp { get; set; }

    /// <summary>
    /// The name of the key that gives the addresses in the host's <c>ASPNETCORE_URLS</c> that use HTTPS a certificate of
    /// <see cref="Tls.TestCertificate"/>, where the host answers HTTP/2 too (ROADMAP 7.3d, ADR-0006); empty for none. Only a host in the
    /// Development environment sets it.
    /// </summary>
    public string? HttpsKey { get; set; }

    /// <summary>
    /// Backoffice user groups to create before the users; existing aliases are left as they are, and each group's global
    /// grant is set to its <see cref="TestUserGroup.Permissions"/>.
    /// </summary>
    public IList<TestUserGroup> UserGroups { get; } = [];

    /// <summary>Restricted root folders to create, each with its own grant to one group, after the user groups.</summary>
    public IList<TestRestrictedFolder> RestrictedFolders { get; } = [];

    /// <summary>Backoffice users to create; existing email addresses are left as they are.</summary>
    public IList<TestUser> Users { get; } = [];

    /// <summary>Members to create, with their groups; existing email addresses are left as they are.</summary>
    public IList<TestMember> Members { get; } = [];
}
