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

    /// <summary>Backoffice user groups to create before the users; existing aliases are left as they are.</summary>
    public IList<TestUserGroup> UserGroups { get; } = [];

    /// <summary>Backoffice users to create; existing email addresses are left as they are.</summary>
    public IList<TestUser> Users { get; } = [];

    /// <summary>Members to create, with their groups; existing email addresses are left as they are.</summary>
    public IList<TestMember> Members { get; } = [];
}
