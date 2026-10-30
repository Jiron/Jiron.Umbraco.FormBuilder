using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags;
using Jiron.Umbraco.FormBuilder.Tests.Unit.OptionLists;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Entries;

/// <summary>
/// Prefill as the reader takes it (ADR-0124): a field hidden behind the signed-in member's value takes that value
/// whatever was posted, where it passes the field's checks, and the visitor's answer everywhere else; a field shown with
/// the member's value stores what was posted; a hidden field takes the member's value, then the context value, then its
/// default.
/// </summary>
public sealed class SubmissionReaderPrefillTests
{
    private static readonly Guid Email = Key(1);
    private static readonly Guid Name = Key(2);
    private static readonly Guid Code = Key(3);
    private static readonly Guid Topic = Key(4);
    private static readonly Guid Sender = Key(5);
    private static readonly Guid Member = Key(40);

    // A contact form: a required email address hidden behind the member's, a name shown with the member's, a code of
    // digits hidden behind the member's name (which never fits it), and two hidden fields, the topic from the page's
    // context value and the sender from the member's name, else the context value.
    private static FormDefinition Form { get; } = new()
    {
        Key = Key(100),
        Pages =
        [
            new FormPage
            {
                Key = Key(101),
                Groups =
                [
                    new FormGroup
                    {
                        Key = Key(102),
                        Fields =
                        [
                            Field(Email, "email", "email") with { Required = true, Prefill = new FieldPrefill { Member = "email", MemberMode = PrefillMemberMode.Hide } },
                            Field(Name, "text", "name") with { Prefill = new FieldPrefill { Member = "name" } },
                            Field(Code, "text", "code") with { Validation = [new PatternCheck { Pattern = "[0-9]+" }], Prefill = new FieldPrefill { Member = "name", MemberMode = PrefillMemberMode.Hide } },
                            Field(Topic, "hidden", "topic") with { DefaultValue = FieldDefaultValue.Of("general"), Prefill = new FieldPrefill { Context = "topic" } },
                            Field(Sender, "hidden", "sender") with { DefaultValue = FieldDefaultValue.Of("nobody"), Prefill = new FieldPrefill { Member = "name", Context = "topic" } },
                        ],
                    },
                ],
            },
        ],
        Settings = new FormSettings { Developer = new FormDeveloperSettings { ContextValues = ["topic"] } },
    };

    [Test]
    public async Task AFieldHiddenBehindTheMembersValue_TakesIt_WhateverWasPosted()
    {
        var read = await ReadAsync(Post((Email, "forged@example.com"), (Code, "42")), member: true);

        Assert.Multiple(() =>
        {
            Assert.That(read.Errors, Is.Empty);
            Assert.That(Value(read, Email), Is.EqualTo("ada@example.com"), "the member's address, never the posted one");
            Assert.That(read.ForMergeTags.Find(Email)?.Values.Single().Text, Is.EqualTo("ada@example.com"), "the actions' merge tags show it");
        });
    }

    [Test]
    public async Task AFieldHiddenBehindTheMembersValue_TakesTheVisitorsAnswer_WithoutAMember()
    {
        var answered = await ReadAsync(Post((Email, "visitor@example.com"), (Code, "42")), member: false);
        var missing = await ReadAsync(Post((Code, "42")), member: false);

        Assert.Multiple(() =>
        {
            Assert.That(Value(answered, Email), Is.EqualTo("visitor@example.com"));
            Assert.That(answered.Errors, Is.Empty);
            Assert.That(missing.Errors.Select(error => (error.Field, error.Kind)), Is.EqualTo(new[] { (Email, FieldMessageKinds.Required) }), "the page asked them, so it is still required");
        });
    }

    [Test]
    public async Task AMembersValueTheFieldsChecksRefuse_LeavesThePostedAnswer()
    {
        var answered = await ReadAsync(Post((Code, "42")), member: true);
        var wrong = await ReadAsync(Post((Code, "forty-two")), member: true);

        Assert.Multiple(() =>
        {
            Assert.That(Value(answered, Code), Is.EqualTo("42"), "the member's name is no code, so the page showed the field");
            Assert.That(wrong.Errors.Select(error => (error.Field, error.Check)), Is.EqualTo(new (Guid, int?)[] { (Code, 0) }), "and checks the answer as any other");
        });
    }

    [Test]
    public async Task AFieldShownWithTheMembersValue_StoresWhatWasPosted()
    {
        var read = await ReadAsync(Post((Name, "Ada L."), (Code, "42")), member: true);

        Assert.That(Value(read, Name), Is.EqualTo("Ada L."));
    }

    [Test]
    public async Task AHiddenField_TakesTheMembersValue_ThenTheContextValue_ThenItsDefault()
    {
        var signedIn = await ReadAsync(Post((Code, "42"), (Sender, "forged"), (Topic, "forged")), member: true, topic: "billing");
        var anonymous = await ReadAsync(Post((Email, "visitor@example.com"), (Code, "42")), member: false, topic: "billing");
        var plain = await ReadAsync(Post((Email, "visitor@example.com"), (Code, "42")), member: false);

        Assert.Multiple(() =>
        {
            Assert.That((Value(signedIn, Sender), Value(signedIn, Topic)), Is.EqualTo(("Ada Lovelace", "billing")), "never the posted values");
            Assert.That((Value(anonymous, Sender), Value(anonymous, Topic)), Is.EqualTo(("billing", "billing")));
            Assert.That((Value(plain, Sender), Value(plain, Topic)), Is.EqualTo(("nobody", "general")));
        });
    }

    private static PostedSubmission Post(params (Guid Field, string Text)[] answers) => new()
    {
        Scope = MergeTagTestSite.Scope(form: Form),
        Now = new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero),
        TimeZone = "Europe/Zurich",
        Fields = answers.ToDictionary(answer => answer.Field, answer => new PostedAnswer([answer.Text])),
    };

    private static async Task<SubmissionAnswers> ReadAsync(PostedSubmission post, bool member, string? topic = null)
    {
        var umbraco = new TestUmbraco();
        umbraco.Member(Member, "Ada Lovelace", "ada@example.com");
        var options = new OptionListTestSite().Options();
        var site = MergeTagTestSite.Create(umbraco: umbraco, options: options);
        var scope = post.Scope with
        {
            Member = member ? Member : null,
            Context = topic is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["topic"] = topic },
        };
        return await new SubmissionReader(TestFieldTypes.Registry, site.Service, options, NullLogger<SubmissionReader>.Instance).ReadAsync(post with { Scope = scope });
    }

    private static string? Value(SubmissionAnswers read, Guid field) => read.Answers.SingleOrDefault(answer => answer.Field == field)?.Values.Single().Text;

    private static FormField Field(Guid key, string type, string alias) => new() { Key = key, Type = type, Alias = alias };

    private static Guid Key(int number) => Guid.Parse($"72000000-0000-4000-8000-{number:D12}");
}
