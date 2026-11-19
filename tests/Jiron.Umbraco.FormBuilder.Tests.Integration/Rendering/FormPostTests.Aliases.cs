using System.Net;
using Jiron.Umbraco.FormBuilder.Web.Rendering;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Rendering;

// Fields whose aliases are names of a form element's own members, or of every object's: a control's name is its alias
// (ADR-0015), which a browser's script finds in front of the form's own member of that name, but which the post
// without JavaScript reads as any other name. Each answer is stored.
public sealed partial class FormPostTests
{
    private const string AliasesPath = "/post-aliases/";

    private static readonly string[] MemberAliases = ["id", "action", "method", "submit", "constructor", "toString"];

    [Test]
    public async Task FieldsNamedAsTheFormsOwnMembers_ArePostedWithoutJavaScript_AndEachAnswerIsStored()
    {
        var fields = MemberAliases.Select((alias, at) => Field(K(0xF0 + at), alias, "text")).ToArray();
        var key = await PublishFormAsync("postAliases", definition => WithFields(definition, fields));
        _ = await PagesAsync("post-aliases");
        var form = FormOf(await DocumentAsync(AliasesPath), "jfb-1");
        foreach (var alias in MemberAliases)
        {
            Fill(form, alias, $"answer for {alias}");
        }

        using var sent = await SubmitAsync(form, FormPostFields.Submit, AliasesPath);
        var entries = await StoredAsync(key);
        var catalogue = await CatalogueAsync(key);

        Assert.Multiple(async () =>
        {
            Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.Redirect), await sent.Content.ReadAsStringAsync());
            Assert.That(entries, Has.Count.EqualTo(1));
            foreach (var field in fields)
            {
                Assert.That(entries[0].Where(value => value.Field == catalogue[field.Key]).Select(value => value.Text), Is.EqualTo(new[] { $"answer for {field.Alias}" }), field.Alias);
            }
        });
    }
}
