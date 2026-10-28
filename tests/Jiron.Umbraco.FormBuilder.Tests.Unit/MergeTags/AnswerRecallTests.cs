using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Testing;
using static Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags.MergeTagAnswerForms;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags;

/// <summary>
/// Answer recall on the server (ADR-0068) beyond the shared vectors, which render visitor texts in both runtimes: the
/// form check of a visitor text with the page, group or field whose text it is, and the other places, which take every
/// field, find fields by their former aliases and use a site type's own display and a hidden field's value.
/// </summary>
public sealed class AnswerRecallTests
{
    [Test]
    public void TheCheckOfAVisitorText_RefusesTheFieldItselfAndALaterOne_AtTheirPaths()
    {
        var email = Field("email", "email", "Email");
        var topic = Field("text", "topic", "Topic");
        var remarks = Field("textarea", "remarks", "Remarks");
        var form = Form(email, topic, remarks);
        const string Text = "{{ field.email }}, {{ field.topic }} or {{ field.remarks.label }}?";

        var problems = MergeTagTestSite.Create().Service.Check(Text, MergeTagPlace.VisitorText, MergeTagContext.Text, form, topic.Key);

        Assert.That(problems, Is.EqualTo(new[]
        {
            new MergeTagProblem(MergeTagProblems.FieldAfterText, Text.IndexOf("field.topic", StringComparison.Ordinal)),
            new MergeTagProblem(MergeTagProblems.FieldAfterText, Text.IndexOf("field.remarks", StringComparison.Ordinal)),
        }));
    }

    [Test]
    public void TheCheck_TakesEveryField_ForAVisitorTextAfterEveryField_AndInEveryOtherPlace()
    {
        var email = Field("email", "email", "Email");
        var topic = Field("text", "topic", "Topic");
        var form = Form(email, topic);
        var service = MergeTagTestSite.Create().Service;
        const string Text = "{{ field.email }} {{ field.topic }}";

        Assert.Multiple(() =>
        {
            Assert.That(service.Check(Text, MergeTagPlace.VisitorText, MergeTagContext.Text, form), Is.Empty);
            Assert.That(service.Check(Text, MergeTagPlace.Mail, MergeTagContext.Text, form, email.Key), Is.Empty);
            Assert.That(service.Check(Text, MergeTagPlace.Outcome, MergeTagContext.Html, form, email.Key), Is.Empty);
        });
    }

    [Test]
    public async Task TheFieldSourceWithoutAnAlias_IsAnUnknownPath_RenderedEmptyAndCheckedInEveryPlace()
    {
        var email = Field("email", "email", "Email");
        var form = Form(email);
        var site = MergeTagTestSite.Create();

        var visitor = await site.RenderAsync("[{{ field }}]", Scope(form, Answers(Answer(email, "anna@example.org"))), MergeTagPlace.VisitorText);

        Assert.Multiple(() =>
        {
            Assert.That(visitor.Text, Is.EqualTo("[]"));
            Assert.That(visitor.Problems, Is.EqualTo(new[] { new MergeTagProblem(MergeTagProblems.UnknownPath, 4) }), "a warning, not a failure");
            Assert.That(site.Service.Check("{{ field }}", MergeTagPlace.VisitorText, MergeTagContext.Text, form, email.Key), Is.EqualTo(new[] { new MergeTagProblem(MergeTagProblems.UnknownPath, 3) }));
            Assert.That(site.Service.Check("{{ field }}", MergeTagPlace.VisitorText, MergeTagContext.Text, form), Is.EqualTo(new[] { new MergeTagProblem(MergeTagProblems.UnknownPath, 3) }), "after every field");
            Assert.That(site.Service.Check("{{ field }}", MergeTagPlace.Mail, MergeTagContext.Text, form), Is.EqualTo(new[] { new MergeTagProblem(MergeTagProblems.UnknownPath, 3) }));
        });
    }

    [Test]
    public async Task AFormerAlias_IsUnknownInAVisitorText_AndFindsItsFieldInMail()
    {
        var email = Field("email", "email", "Email") with { FormerAliases = ["mail"] };
        var form = Form(email);
        var scope = Scope(form, Answers(Answer(email, "anna@example.org")));
        var site = MergeTagTestSite.Create();

        var visitor = await site.RenderAsync("[{{ field.mail }}]", scope, MergeTagPlace.VisitorText);
        var mail = await site.RenderAsync("[{{ field.mail }}]", scope);

        Assert.Multiple(() =>
        {
            Assert.That(visitor.Text, Is.EqualTo("[]"));
            Assert.That(visitor.Problems.Select(problem => problem.Code), Is.EqualTo(new[] { MergeTagProblems.UnknownPath }));
            Assert.That(site.Service.Check("{{ field.mail }}", MergeTagPlace.VisitorText, MergeTagContext.Text, form).Select(problem => problem.Code), Is.EqualTo(new[] { MergeTagProblems.UnknownPath }));
            Assert.That(mail.Text, Is.EqualTo("[anna@example.org]"));
        });
    }

    [Test]
    public async Task ASiteTypesOwnDisplay_ShowsInMail_AndItsRuleKindsDefaultInAVisitorText()
    {
        var site = MergeTagTestSite.Create(types: TestFieldTypes.Build(builder => builder.Replace<DropdownFieldType, FruitDropdown>()));
        var fruit = Field("dropdown", "fruit", "Fruit") with { Options = Options(("a", "Apple")) };
        var scope = Scope(Form(fruit), Answers(Answer(fruit, "a")));

        var visitor = await site.RenderAsync("{{ field.fruit }}", scope, MergeTagPlace.VisitorText);
        var mail = await site.RenderAsync("{{ field.fruit }}", scope);

        Assert.Multiple(() =>
        {
            Assert.That(visitor.Text, Is.EqualTo("Apple"), "the browser has no display of the site's own, so neither has the server");
            Assert.That(mail.Text, Is.EqualTo("A fruit: Apple"));
        });
    }

    [Test]
    public async Task AHiddenFieldsValue_ShowsInMail_AndOnlyItsLabelInAVisitorText()
    {
        var campaign = Field("hidden", "campaign", "Campaign");
        var scope = Scope(Form(campaign), Answers(Answer(campaign, "spring")));
        var site = MergeTagTestSite.Create();

        var visitor = await site.RenderAsync("[{{ field.campaign }}|{{ field.campaign.value }}|{{ field.campaign.label }}]", scope, MergeTagPlace.VisitorText);
        var mail = await site.RenderAsync("[{{ field.campaign }}|{{ field.campaign.value }}]", scope);

        Assert.Multiple(() =>
        {
            Assert.That(visitor.Text, Is.EqualTo("[||Campaign]"), "the value stays on the server");
            Assert.That(mail.Text, Is.EqualTo("[spring|spring]"));
        });
    }

    /// <summary>A site's dropdown that writes its own display.</summary>
    private sealed class FruitDropdown : FormBuilderFieldTypeBase<DropdownFieldSettings>
    {
        public FruitDropdown()
            : base(new DropdownFieldType().Declaration)
        {
        }

        protected override void Display(DropdownFieldSettings settings, FieldDisplay display)
        {
            foreach (var value in display.Values)
            {
                display.Add("A fruit: " + display.OptionLabel(value.Text));
            }
        }
    }
}
