using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using static Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags.MergeTagTestSite;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags;

/// <summary>
/// <c>member.*</c> (ADR-0014, ADR-0067): the signed-in member's name, email address and the properties their member
/// type lets them see and does not mark sensitive; nothing without a member.
/// </summary>
public sealed class MemberMergeTagSourceTests
{
    private static readonly Guid Anna = Guid.Parse("70000000-0000-4000-8000-000000000001");

    private static TestUmbraco Members()
    {
        var umbraco = new TestUmbraco();
        umbraco.Language("en-GB", isDefault: true);
        umbraco.Member(Anna, "Anna Muster", "anna@example.com", new Dictionary<string, (object?, bool, bool)>
        {
            ["company"] = ("Lindenhall <Ltd>", true, false),
            ["memberSince"] = (new DateTime(2019, 5, 1, 0, 0, 0, DateTimeKind.Unspecified), true, false),
            ["loans"] = (12, true, false),
            ["staffNote"] = ("Owes a book", false, false),
            ["iban"] = ("CH93 0076 2011 6238 5295 7", true, true),
        });
        return umbraco;
    }

    [Test]
    public async Task TheMembersName_Email_AndAPropertyTheyMaySee_Show()
    {
        var site = Create(umbraco: Members());

        var rendered = await site.RenderAsync(
            "{{ member.name }}|{{ member.email }}|{{ member.company }}|{{ member.memberSince }}|{{ member.loans | number: 1 }}",
            Scope(member: Anna));

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Is.EqualTo("Anna Muster|anna@example.com|Lindenhall <Ltd>|01/05/2019|12.0"));
            Assert.That(rendered.Problems, Is.Empty);
        });
    }

    [Test]
    public async Task AValueOfTheMember_IsEscapedByThePlacesContext()
    {
        var site = Create(umbraco: Members());

        var rendered = await site.RenderAsync("<p>{{ member.company }}</p>", Scope(member: Anna), MergeTagPlace.Outcome, MergeTagContext.Html);

        Assert.That(rendered.Text, Is.EqualTo("<p>Lindenhall &lt;Ltd&gt;</p>"));
    }

    [Test]
    public async Task APropertyTheMemberMayNotSee_OrASensitiveOne_OrOneTheirTypeLacks_IsEmpty()
    {
        var site = Create(umbraco: Members());

        var rendered = await site.RenderAsync("[{{ member.staffNote }}|{{ member.iban }}|{{ member.shoeSize }}]", Scope(member: Anna));

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Is.EqualTo("[||]"));
            Assert.That(rendered.Problems, Is.Empty);
        });
    }

    [Test]
    public async Task WithoutAMember_OrForOneWhoIsGone_EveryPathIsEmpty()
    {
        var site = Create(umbraco: Members());
        const string Template = "[{{ member.name }}|{{ member.email }}|{{ member.company }}]";

        var without = await site.RenderAsync(Template, Scope());
        var gone = await site.RenderAsync(Template, Scope(member: Guid.Parse("70000000-0000-4000-8000-0000000000ff")));

        Assert.Multiple(() =>
        {
            Assert.That(without.Text, Is.EqualTo("[||]"));
            Assert.That(without.Problems, Is.Empty);
            Assert.That(gone.Text, Is.EqualTo("[||]"));
        });
    }

    [Test]
    public async Task APathOfMoreThanOneSegment_IsUnknown_AndThePagesThatMayBeCached_RefuseTheMember()
    {
        var site = Create(umbraco: Members());

        var deep = await site.RenderAsync("{{ member.company.name }}", Scope(member: Anna));
        var cached = await site.RenderAsync("{{ member.name }}", Scope(member: Anna), MergeTagPlace.VisitorText, MergeTagContext.Text);

        Assert.Multiple(() =>
        {
            Assert.That(deep.Problems, Is.EqualTo(new[] { new MergeTagProblem(MergeTagProblems.UnknownPath, 3) }));
            Assert.That(site.Service.Check("{{ member }}", MergeTagPlace.Mail, MergeTagContext.Text, Form()), Is.EqualTo(new[] { new MergeTagProblem(MergeTagProblems.UnknownPath, 3) }));
            Assert.That(cached.Text, Is.EqualTo("{{ member.name }}"), "a visitor text holds nothing per visitor");
            Assert.That(cached.Problems.Select(problem => problem.Code), Is.EqualTo(new[] { MergeTagProblems.SourceNotForPlace }));
        });
    }
}
