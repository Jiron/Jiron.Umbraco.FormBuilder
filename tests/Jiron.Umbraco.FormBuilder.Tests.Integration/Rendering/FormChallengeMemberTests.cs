using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Web.PublicApi;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Rendering;

/// <summary>
/// The challenge with a member signed in (ROADMAP 7.1b, ADR-0004, ADR-0012, ADR-0138): a form for members binds it to the
/// member and gives it to no one else; another member's, a visitor's or none is refused on entries, page checks and
/// uploads; and it tells a member only what the form's rules ask. A form for members takes its entries from 14.3 on, so
/// until then the right challenge meets <c>member_required</c> in the place of the entry.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class FormChallengeMemberTests : RenderingTestBase
{
    private const string Entry = """{"revision":1,"values":{"name":"Ada"}}""";

    private static readonly FormField Attachment = new()
    {
        Key = Guid.Parse("71b00000-0000-4000-8000-000000000020"),
        Type = "file",
        Alias = "attachment",
        Label = new LocalizedText("Attachment"),
        Settings = SettingsObject.Parse("""{"types":["pdf"],"maxBytes":2048}"""),
    };

    private ChallengeTokens Tokens => GetRequiredService<ChallengeTokens>();

    [Test]
    public async Task AFormForMembers_BindsItsChallengeToTheMemberSignedIn()
    {
        var key = await PublishFormAsync("forMembers", ForMembers, NameField, Attachment);
        _ = await PagesAsync("limited");
        await SignInMemberAsync("member@example.com");
        var member = GetRequiredService<IMemberService>().GetByUsername(MemberName)!.Key;

        var token = await ChallengeAsync(key);

        Assert.That(Tokens.Read(token, key, TimeSpan.Zero)!.Member, Is.EqualTo(member));
    }

    [Test]
    public async Task AnEntry_ToAFormForAnyone_FromASignedInMember_KeepsTheMember_AndAVisitorsKeepsNone()
    {
        var key = await PublishFormAsync("forAnyone", null, NameField);
        _ = await PagesAsync("open");
        using var visitor = await PostAsync(key, "entries", Entry, await ChallengeAsync(key));
        await SignInMemberAsync("member@example.com");
        var member = GetRequiredService<IMemberService>().GetByUsername(MemberName)!.Key;
        using var signedIn = await PostAsync(key, "entries", Entry, await ChallengeAsync(key));

        var entries = GetRequiredService<IEntryRepository>();
        var row = (await GetRequiredService<IFormRepository>().GetAsync(key))!;
        var stored = new List<StoredEntry>();
        foreach (var id in (await entries.ListIdsAsync(row.Id, 10)).Order())
        {
            stored.Add((await entries.GetByIdAsync(id))!);
        }

        Assert.Multiple(() =>
        {
            Assert.That((visitor.StatusCode, signedIn.StatusCode), Is.EqualTo((HttpStatusCode.OK, HttpStatusCode.OK)));
            Assert.That(stored.Select(entry => entry.MemberKey), Is.EqualTo(new Guid?[] { null, member }), "the member signed in when it was sent (ADR-0009, ADR-0223)");
        });
    }

    [TestCase("entries", Entry)]
    [TestCase("validate", """{"revision":1,"pageKey":"00000000-0000-4000-8000-000000000001","values":{}}""")]
    public async Task OnAFormForMembers_AnotherMembersChallenge_AVisitorsAndNone_AreRefused(string endpoint, string body)
    {
        var key = await PublishFormAsync("forMembers", ForMembers, NameField, Attachment);
        _ = await PagesAsync("limited");
        await SignInMemberAsync("member@example.com");
        var answers = new List<string>();
        foreach (var challenge in new[] { Tokens.Issue(key, Guid.NewGuid()), Tokens.Issue(key, null), null })
        {
            using var answer = await PostAsync(key, endpoint, body, challenge);
            answers.Add(await RefusalAsync(answer));
        }

        using var own = await PostAsync(key, endpoint, body, await ChallengeAsync(key));

        Assert.Multiple(async () =>
        {
            Assert.That(answers, Is.All.EqualTo("401 challenge_required"));
            Assert.That(await RefusalAsync(own), Is.EqualTo("403 member_required"), "its own challenge passes the gate; 14.3 admits its members");
        });
    }

    [Test]
    public async Task AnUpload_ToAFormForMembers_NeedsAMember_AndTheirOwnChallenge()
    {
        var key = await PublishFormAsync("forMembers", ForMembers, NameField, Attachment);
        _ = await PagesAsync("limited");
        using var visitor = await UploadAsync(key, Tokens.Issue(key, null));
        await SignInMemberAsync("member@example.com");
        using var another = await UploadAsync(key, Tokens.Issue(key, Guid.NewGuid()));
        using var anonymous = await UploadAsync(key, Tokens.Issue(key, null));
        using var own = await UploadAsync(key, await ChallengeAsync(key));

        Assert.Multiple(async () =>
        {
            Assert.That(await RefusalAsync(visitor), Is.EqualTo("403 member_required"));
            Assert.That(await RefusalAsync(another), Is.EqualTo("401 challenge_required"));
            Assert.That(await RefusalAsync(anonymous), Is.EqualTo("401 challenge_required"));
            Assert.That(own.StatusCode, Is.EqualTo(HttpStatusCode.OK), await own.Content.ReadAsStringAsync());
        });
    }

    [Test]
    public async Task AServerKey_GetsNoChallenge_AndSendsNoFile_ToAFormForMembers_EvenWithAMemberSignedIn()
    {
        var key = await PublishFormAsync("forMembers", ForMembers, NameField, Attachment);
        _ = await PagesAsync("limited");
        await SignInMemberAsync("member@example.com");

        using var challenge = await SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/challenge", request =>
        {
            request.Headers.TryAddWithoutValidation("Authorization", "Jfb-Key " + TestServerKey);
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        });
        using var upload = await SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/uploads?fieldKey={Attachment.Key}", request =>
        {
            request.Headers.TryAddWithoutValidation("Authorization", "Jfb-Key " + TestServerKey);
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        });

        Assert.Multiple(async () =>
        {
            Assert.That(await RefusalAsync(challenge), Is.EqualTo("403 member_required"));
            Assert.That(await RefusalAsync(upload), Is.EqualTo("403 member_required"));
        });
    }

    [Test]
    public async Task AFormForMembers_GivesNoChallenge_ToAMemberOutsideItsGroups()
    {
        var group = await GroupAsync("Readers");
        var key = await PublishFormAsync(
            "forMembers",
            definition => definition with
            {
                Settings = definition.Settings with { Availability = definition.Settings.Availability with { Access = MemberAccess.Groups, MemberGroups = [group] } },
            },
            NameField,
            Attachment);
        _ = await PagesAsync("limited");
        await SignInMemberAsync("member@example.com");

        using var outside = await PostAsync(key, "challenge", "{}", null, withChallenge: false);
        using var fileOutside = await UploadAsync(key, Tokens.Issue(key, null));
        GetRequiredService<IMemberService>().AssignRole(MemberName, "Readers");
        using var inside = await PostAsync(key, "challenge", "{}", null, withChallenge: false);

        Assert.Multiple(async () =>
        {
            Assert.That(await RefusalAsync(outside), Is.EqualTo("403 member_required"));
            Assert.That(await RefusalAsync(fileOutside), Is.EqualTo("403 member_required"));
            Assert.That(inside.StatusCode, Is.EqualTo(HttpStatusCode.OK), await inside.Content.ReadAsStringAsync());
        });
    }

    [Test]
    public async Task AMember_HearsOnlyWhatTheFormsRulesAsk()
    {
        var readers = await GroupAsync("Readers");
        _ = await GroupAsync("Writers");
        var key = await PublishFormAsync("withRules", definition => WithFields(definition, NameField with
        {
            Rules =
            [
                new FormRule
                {
                    Key = Guid.Parse("71b00000-0000-4000-8000-000000000021"),
                    Effect = RuleEffect.Show,
                    Conditions = [new RuleCondition { Context = "memberGroup", Operator = "isOneOf", Value = new ConditionOperand.List([new ConditionOperand.Text("Readers"), new ConditionOperand.Text("Writers"), new ConditionOperand.Text(readers.ToString("D"))]) }],
                },
            ],
        }));
        var plain = await PublishFormAsync("withoutRules");
        _ = await PagesAsync("limited");

        using var visitor = await PostAsync(key, "challenge", "{}", null, withChallenge: false);
        await SignInMemberAsync("member@example.com");
        GetRequiredService<IMemberService>().AssignRole(MemberName, "Readers");
        using var member = await PostAsync(key, "challenge", "{}", null, withChallenge: false);
        using var noRules = await PostAsync(plain, "challenge", "{}", null, withChallenge: false);

        Assert.Multiple(async () =>
        {
            Assert.That((await JsonAsync(visitor))["member"], Is.Null, "a visitor");
            var told = (await JsonAsync(member))["member"]!;
            Assert.That(told["signedIn"]!.GetValue<bool>(), Is.True);
            Assert.That(told["groups"]!.AsArray().Select(group => group!.GetValue<string>()), Is.EquivalentTo(new[] { "Readers", readers.ToString("D") }), "of the groups the rules name, by name or by key, the member's");
            Assert.That((await JsonAsync(noRules))["member"], Is.Null, "no rule asks");
            Assert.That(Tokens.Read((await JsonAsync(member))["challenge"]!.GetValue<string>(), key, TimeSpan.Zero)!.Member, Is.Null, "a form for anyone binds no member");
        });
    }

    private static FormDefinition ForMembers(FormDefinition definition) => definition with
    {
        Settings = definition.Settings with { Availability = definition.Settings.Availability with { Access = MemberAccess.Members } },
    };

    private async Task<Guid> GroupAsync(string name)
    {
        var created = await GetRequiredService<IMemberGroupService>().CreateAsync(new MemberGroup { Name = name });
        Assert.That(created.Success, Is.True);
        return created.Result!.Key;
    }

    private Task<HttpResponseMessage> PostAsync(Guid key, string endpoint, string body, string? challenge, bool withChallenge = true)
    {
        var posted = JsonNode.Parse(body)!.AsObject();
        if (withChallenge)
        {
            posted["challenge"] = challenge;
        }

        return SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/{endpoint}", request => request.Content = new StringContent(posted.ToJsonString(), Encoding.UTF8, "application/json"));
    }

    private Task<HttpResponseMessage> UploadAsync(Guid key, string challenge) =>
        SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/uploads?fieldKey={Attachment.Key}", request =>
        {
            var content = new MultipartFormDataContent { { new StringContent(challenge), "challenge" } };
            var file = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.7 a file"));
            file.Headers.TryAddWithoutValidation("Content-Disposition", "form-data; name=\"file\"; filename=\"a.pdf\"");
            content.Add(file);
            request.Content = content;
        });
}
