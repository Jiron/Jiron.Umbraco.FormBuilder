namespace Jiron.Umbraco.FormBuilder.Testing.People;

/// <summary>A backoffice user group a test creates.</summary>
public sealed class TestUserGroup
{
    /// <summary>The group alias, for example <c>jfbDesigners</c>.</summary>
    public string Alias { get; set; } = string.Empty;

    /// <summary>The display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Section aliases the group may open, for example <c>content</c>.</summary>
    public IList<string> Sections { get; } = [];

    /// <summary>
    /// Whether the group may pick from the whole media library, as a designer choosing a card's image does; none of it
    /// by default, as Umbraco's own new groups.
    /// </summary>
    public bool MediaRootAccess { get; set; }

    /// <summary>
    /// Whether the group may browse the whole content tree (its root as the start node, and Umbraco's Browse
    /// permission), as a designer choosing the page whose children are a field's options does; none of it by default,
    /// as Umbraco's own new groups.
    /// </summary>
    public bool ContentRootAccess { get; set; }

    /// <summary>
    /// Whether the group may also create, update and publish everywhere in the content tree (Umbraco's own permissions),
    /// as a designer who sets up a content action needs where it creates and publishes (ADR-0235); read only with
    /// <see cref="ContentRootAccess"/>, browsing only by default.
    /// </summary>
    public bool ContentEditing { get; set; }

    /// <summary>
    /// The group's global Jiron Form Builder permissions by name, for example <c>viewEntries</c> (ADR-0013); the seed
    /// sets the group's global grant to exactly these, none by default.
    /// </summary>
    public IList<string> Permissions { get; } = [];
}

/// <summary>A restricted folder at the root that a test host seeds, with its own grant to one group (ADR-0013, ADR-0049).</summary>
public sealed class TestRestrictedFolder
{
    /// <summary>The folder's name, for example <c>HR</c>; the seed takes the root folder of that name outside the trash.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The alias of the user group the folder is granted to.</summary>
    public string Group { get; set; } = string.Empty;

    /// <summary>The group's permissions on the folder by name; the seed sets its grant to exactly these.</summary>
    public IList<string> Permissions { get; } = [];
}

/// <summary>A backoffice user a test creates, approved and with the given password.</summary>
public sealed class TestUser
{
    /// <summary>The email address, also the user name.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>The display name; the email address when empty.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The password. Generated per run (<see cref="TestPeople.NewPassword"/>), never written to the repository.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Aliases of the groups the user belongs to.</summary>
    public IList<string> Groups { get; } = [];

    /// <summary>
    /// The backoffice language the user's account keeps on the server, an ISO code such as <c>de-DE</c>; Umbraco's
    /// default when empty. What the server writes in the user's language (an alert, a digest, a sample mail) follows it.
    /// </summary>
    public string Language { get; set; } = string.Empty;
}

/// <summary>A member a test creates, approved, of Umbraco's default member type, with the given password.</summary>
public sealed class TestMember
{
    /// <summary>The email address, also the user name.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>The display name; the email address when empty.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The password. Generated per run (<see cref="TestPeople.NewPassword"/>), never written to the repository.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Names of the member groups the member belongs to; missing groups are created.</summary>
    public IList<string> Groups { get; } = [];
}
