using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Testing;
using static Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags.MergeTagAnswerForms;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags;

/// <summary>
/// The <c>field</c> source (ADR-0014, ADR-0066): for every built-in type that holds a value, the answer as the type
/// shows it, its values in the wire form and the field's label; a repeatable group's child as a list across its rows;
/// former aliases; and what the rules hid, empty.
/// </summary>
public sealed class FieldMergeTagSourceTests
{
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    /// <summary>One answer of each built-in type, in British English, with what <c>{{ field.answer }}</c> and <c>.value</c> render.</summary>
    private static IEnumerable<TestCaseData> Types()
    {
        TestCaseData Case(string name, FormField field, IReadOnlyList<MergeTagAnswer> answers, string display, string? value, params FormField[] more) =>
            new TestCaseData(field, answers, display, value, more).SetName($"{{m}}({name})");

        var text = Field("text", "answer", "The answer");
        yield return Case("text", text, [Answer(text, "Hello")], "Hello", "Hello");

        var textarea = Field("textarea", "answer", "The answer");
        yield return Case("textarea", textarea, [Answer(textarea, "Line 1\nLine 2")], "Line 1\nLine 2", "Line 1\nLine 2");

        var email = Field("email", "answer", "The answer");
        yield return Case("email", email, [Answer(email, "anna@example.org")], "anna@example.org", "anna@example.org");

        var phone = Field("phone", "answer", "The answer");
        yield return Case("phone", phone, [Answer(phone, "+41 44 000 00 00")], "+41 44 000 00 00", "+41 44 000 00 00");

        var url = Field("url", "answer", "The answer");
        yield return Case("url", url, [Answer(url, "https://example.org/a?b=c")], "https://example.org/a?b=c", "https://example.org/a?b=c");

        var number = Field("number", "answer", "The answer", """{"decimals":2,"prefix":{"":"CHF"},"suffix":{"":"net"}}""");
        yield return Case("number", number, [Answer(number, "1234.5")], "CHF 1,234.5 net", "1234.5");

        var date = Field("date", "answer", "The answer");
        yield return Case("date", date, [Answer(date, "2026-03-07")], "07/03/2026", "2026-03-07");

        var time = Field("time", "answer", "The answer");
        yield return Case("time", time, [Answer(time, "14:30")], "14:30", "14:30");

        var dateTime = Field("datetime", "answer", "The answer");
        yield return Case("datetime", dateTime, [Answer(dateTime, "2026-03-07T14:30")], "07/03/2026 14:30", "2026-03-07T14:30");

        var rating = Field("rating", "answer", "The answer");
        yield return Case("rating", rating, [Answer(rating, "4")], "4", "4");

        var scale = Field("scale", "answer", "The answer");
        yield return Case("scale", scale, [Answer(scale, "7")], "7", "7");

        var slider = Field("slider", "answer", "The answer", """{"unit":{"":"%"}}""");
        yield return Case("slider", slider, [Answer(slider, "1075")], "1,075 %", "1075");

        var calculation = Field("calculation", "answer", "The answer", """{"formula":"1 + 1","format":{"prefix":{"":"EUR"}}}""");
        yield return Case("calculation", calculation, [Answer(calculation, "12.5")], "EUR 12.50", "12.5");

        var dropdown = Field("dropdown", "answer", "The answer") with { Options = Options(("a", "Apple"), ("b", "Banana")) };
        yield return Case("dropdown", dropdown, [Answer(dropdown, "b")], "Banana", "b");

        var radio = Field("radio", "answer", "The answer") with { Options = Options(("a", "Apple"), ("b", "Banana")) };
        yield return Case("radio with a text of the visitor's own", radio, [Answer(radio, "Quince")], "Quince", "Quince");

        var checkboxes = Field("checkboxes", "answer", "The answer") with { Options = Options(("a", "Apple"), ("b", "Banana"), ("c", "Cherry")) };
        yield return Case("checkboxes", checkboxes, [Answer(checkboxes, "a", "c")], "Apple, Cherry", "a, c");

        var service = NewKey();
        var speed = NewKey();
        var grid = Field("grid", "answer", "The answer") with
        {
            Options = Options(("g", "Good"), ("p", "Poor")),
            Rows = [new GridRow { Key = service, Label = new LocalizedText("Service") }, new GridRow { Key = speed, Label = new LocalizedText("Speed") }],
        };
        yield return Case("grid", grid, [new MergeTagAnswer(grid.Key, [new FieldValue("g", service), new FieldValue("p", speed)])], "Service: Good, Speed: Poor", "g, p");

        var scaleGrid = Field("grid", "answer", "The answer", """{"multiple":true,"scale":{"from":1,"to":5}}""") with
        {
            Rows = [new GridRow { Key = service, Label = new LocalizedText("Service") }, new GridRow { Key = speed }],
        };
        yield return Case(
            "grid on a scale, several columns and a row without a statement",
            scaleGrid,
            [new MergeTagAnswer(scaleGrid.Key, [new FieldValue("4", service), new FieldValue("5", service), new FieldValue("2", speed)])],
            "Service: 4 / 5, 2",
            "4, 5, 2");

        var ranking = Field("ranking", "answer", "The answer") with { Options = Options(("a", "Apple"), ("b", "Banana"), ("c", "Cherry")) };
        yield return Case("ranking", ranking, [Answer(ranking, "c", "a", "b")], "Cherry, Apple, Banana", "c, a, b");

        var checkbox = Field("checkbox", "answer", "The answer");
        yield return Case("checkbox", checkbox, [Answer(checkbox, "true")], "Yes", "true");

        var yesNo = Field("yesno", "answer", "The answer", """{"yesLabel":{"":"Sure"}}""");
        yield return Case("yesno with its own label", yesNo, [Answer(yesNo, "yes")], "Sure", "yes");

        var plainYesNo = Field("yesno", "answer", "The answer", """{"yesLabel":{"":"Sure"}}""");
        yield return Case("yesno without a label of its own", plainYesNo, [Answer(plainYesNo, "no")], "No", "no");

        var consent = Field("consent", "answer", "The answer", """{"text":{"":"<p>I agree.</p>"}}""");
        yield return Case("consent", consent, [new MergeTagAnswer(consent.Key, [new FieldValue("true") { StoredText = Hash }])], "Yes", "true");

        var country = Field("country", "answer", "The answer");
        yield return Case("country", country, [Answer(country, "CH")], "Switzerland", "CH");

        var name = Field("name", "answer", "The answer", """{"parts":["title","first","last"]}""");
        yield return Case("name", name, [Answer(name, """{"title":"Dr","first":"Anna","last":"Muster"}""")], "Dr Anna Muster", """{"title":"Dr","first":"Anna","last":"Muster"}""");

        var address = Field("address", "answer", "The answer", """{"parts":["country"]}""");
        var where = """{"street":"Bahnhofstrasse 1","postcode":"8001","city":"Zurich","country":"CH"}""";
        yield return Case("address", address, [Answer(address, where)], "Bahnhofstrasse 1, 8001 Zurich, Switzerland", where);

        var file = Field("file", "answer", "The answer");
        yield return Case(
            "file",
            file,
            [new MergeTagAnswer(file.Key, [new FieldValue("upload-1"), new FieldValue("upload-2")], [new FieldFile("plan.pdf", 1200), new FieldFile("photo.jpg", 5600)])],
            "plan.pdf, photo.jpg",
            "upload-1, upload-2");

        var signature = Field("signature", "answer", "The answer");
        yield return Case("signature", signature, [new MergeTagAnswer(signature.Key, [new FieldValue("upload-3")], [new FieldFile("signature.png", 900)])], "signature.png", "upload-3");

        var hidden = Field("hidden", "answer", "The answer");
        yield return Case("hidden", hidden, [Answer(hidden, "campaign-7")], "campaign-7", "campaign-7");

        var childName = Field("text", "childName", "Name");
        var childAge = Field("number", "childAge", "Age");
        var repeater = Field("repeater", "answer", "The answer") with { Fields = [childName, childAge] };
        yield return Case(
            "repeater",
            repeater,
            [InRow(childName, 0, "Anna"), InRow(childAge, 0, "7"), InRow(childName, 1, "Ben")],
            "Name: Anna; Age: 7, Name: Ben",
            null);
    }

    [TestCaseSource(nameof(Types))]
    public async Task EveryBuiltInType_ShowsItsAnswer_ItsValues_AndItsLabel(FormField field, IReadOnlyList<MergeTagAnswer> answers, string display, string? value, FormField[] more)
    {
        var site = MergeTagTestSite.Create();
        var scope = Scope(Form([field, .. more]), new MergeTagAnswers(answers));

        var shown = await site.RenderAsync("{{ field.answer }}", scope);
        var values = await site.RenderAsync("{{ field.answer.value }}", scope);
        var label = await site.RenderAsync("{{ field.answer.label }}", scope);

        Assert.Multiple(() =>
        {
            Assert.That(shown.Text, Is.EqualTo(display), "the answer as shown");
            Assert.That(shown.Problems, Is.Empty, "the answer's problems");
            Assert.That(values.Text, Is.EqualTo(value ?? string.Empty), "the values");
            Assert.That(values.Problems.Select(problem => problem.Code), value is null ? Is.EqualTo(new[] { MergeTagProblems.UnknownPath }) : Is.Empty, "the values' problems");
            Assert.That(label.Text, Is.EqualTo("The answer"), "the label");
        });
    }

    [Test]
    public void TheTypes_WithACase_AreEveryBuiltInTypeThatHoldsAValue()
    {
        var covered = Types().Select(data => ((FormField)data.Arguments[0]!).Type).ToHashSet(StringComparer.Ordinal);
        var holding = TestFieldTypes.Registry.All.Where(type => type.HoldsValue).Select(type => type.Alias);

        Assert.That(covered, Is.EquivalentTo(holding));
    }

    [Test]
    public async Task AnAnswer_IsShownInTheCulture_OptionLabelsWordsNumbersDatesAndCountries()
    {
        var dropdown = Field("dropdown", "fruit", "Fruit") with
        {
            Options = new FieldOptions { Items = [new FieldOption { Value = "a", Label = Text(("", "Apple"), ("de", "Apfel")) }] },
        };
        var amount = Field("number", "amount", "Amount", """{"decimals":2}""");
        var day = Field("date", "day", "Day");
        var when = Field("datetime", "when", "When");
        var country = Field("country", "country", "Country");
        var agreed = Field("checkbox", "agreed", "Agreed");
        var sure = Field("yesno", "sure", "Sure");
        var form = Form(dropdown, amount, day, when, country, agreed, sure);
        var answers = Answers(
            Answer(dropdown, "a"),
            Answer(amount, "1234.5"),
            Answer(day, "2026-03-07"),
            Answer(when, "2026-03-07T14:30"),
            Answer(country, "FR"),
            Answer(agreed, "true"),
            Answer(sure, "no"));
        const string Template = "{{ field.fruit }}|{{ field.amount }}|{{ field.day }}|{{ field.when }}|{{ field.country }}|{{ field.agreed }}|{{ field.sure }}";
        var site = MergeTagTestSite.Create();

        var german = await site.RenderAsync(Template, Scope(form, answers, "de-CH"));
        var french = await site.RenderAsync(Template, Scope(form, answers, "fr-FR"));
        var invariant = await site.RenderAsync(Template, Scope(form, answers, string.Empty));

        Assert.Multiple(() =>
        {
            Assert.That(german.Text, Is.EqualTo("Apfel|1" + (char)0x2019 + "234.5|07.03.2026|07.03.2026 14:30|Frankreich|Ja|Nein"));
            Assert.That(french.Text, Is.EqualTo("Apple|1" + (char)0x202F + "234,5|07/03/2026|07/03/2026 14:30|France|Yes|No"), "French gets its words in 14.2");
            Assert.That(invariant.Text, Is.EqualTo("Apple|1,234.5|03/07/2026|03/07/2026 14:30|France|Yes|No"));
        });
    }

    [Test]
    public async Task ANumberOrDateShown_KeepsItsWireFormBehind_ForTheFilters()
    {
        var amount = Field("number", "amount", "Amount", """{"prefix":{"":"CHF"}}""");
        var day = Field("date", "day", "Day");
        var scope = Scope(Form(amount, day), Answers(Answer(amount, "1234.5"), Answer(day, "2026-03-07")));

        var rendered = await MergeTagTestSite.Create().RenderAsync(
            "{{ field.amount | number: 2 }} {{ field.amount | currency: \"EUR\" }} {{ field.day | date: \"long\" }} {{ field.amount | json }}",
            scope,
            context: MergeTagContext.Text);

        Assert.That(rendered.Text, Is.EqualTo("1,234.50 EUR" + (char)0xA0 + "1,234.50 Saturday, 7 March 2026 1234.5"));
    }

    [Test]
    public async Task AFieldWithSeveralValues_IsAList_EvenWithOneOrNone()
    {
        var colours = Field("checkboxes", "colours", "Colours") with { Options = Options(("r", "Red"), ("g", "Green")) };
        var site = MergeTagTestSite.Create();

        var one = await site.RenderAsync("{{ field.colours | json }} {{ field.colours.value | json }}", Scope(Form(colours), Answers(Answer(colours, "r"))));
        var none = await site.RenderAsync("{{ field.colours | json }}|{{ field.colours | default: \"none\" }}", Scope(Form(colours), Answers()));
        var joined = await site.RenderAsync("{{ field.colours | join: \" and \" }}", Scope(Form(colours), Answers(Answer(colours, "r", "g"))));

        Assert.Multiple(() =>
        {
            Assert.That(one.Text, Is.EqualTo("""["Red"] ["r"]"""));
            Assert.That(none.Text, Is.EqualTo("[]|none"));
            Assert.That(joined.Text, Is.EqualTo("Red and Green"));
        });
    }

    [Test]
    public async Task AValueWithParts_GivesEachPart_AndAPartNotGivenIsEmpty()
    {
        var name = Field("name", "person", "Person", """{"parts":["first","middle","last"]}""");
        var scope = Scope(Form(name), Answers(Answer(name, """{"first":"Anna","last":"Muster"}""")));

        var rendered = await MergeTagTestSite.Create().RenderAsync("{{ field.person.first }}|{{ field.person.middle }}|{{ field.person.last }}", scope);

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Is.EqualTo("Anna||Muster"));
            Assert.That(rendered.Problems, Is.Empty);
        });
    }

    [Test]
    public async Task ARepeatableGroupsChild_IsAListAcrossItsRows_ByItsGroupOrByItsOwnAlias()
    {
        var childName = Field("text", "childName", "Name");
        var childAge = Field("number", "childAge", "Age");
        var children = Field("repeater", "children", "Children") with { Fields = [childName, childAge] };
        var answers = Answers(InRow(childName, 0, "Anna"), InRow(childAge, 0, "7"), InRow(childName, 1, "Ben"), InRow(childAge, 2, "3"));
        var scope = Scope(Form(children), answers);
        var site = MergeTagTestSite.Create();

        var names = await site.RenderAsync("{{ field.children.childName }}|{{ field.childName | join: \"; \" }}|{{ field.children.childName | json }}", scope);
        var ages = await site.RenderAsync("{{ field.children.childAge | number: 1 }}|{{ field.childAge.value | json }}|{{ field.children.childAge.label }}", scope);
        var rows = await site.RenderAsync("{{ field.children | join: \" / \" }}|{{ field.children.label }}", scope);

        Assert.Multiple(() =>
        {
            Assert.That(names.Text, Is.EqualTo("""Anna, Ben|Anna; Ben|["Anna","Ben"]"""));
            Assert.That(ages.Text, Is.EqualTo("7.0, 3.0|[7,3]|Age"));
            Assert.That(rows.Text, Is.EqualTo("Name: Anna; Age: 7 / Name: Ben / Age: 3|Children"));
            Assert.That(names.Problems.Concat(ages.Problems).Concat(rows.Problems), Is.Empty);
        });
    }

    [Test]
    public async Task AFormerAlias_ResolvesToItsField_AfterTheFieldWhoseAliasItIs()
    {
        var email = Field("email", "email", "Email") with { FormerAliases = ["mail", "contact"] };
        var contact = Field("text", "contact", "Contact");
        var childName = Field("text", "childName", "Name") with { FormerAliases = ["kidName"] };
        var children = Field("repeater", "children", "Children") with { Fields = [childName], FormerAliases = ["kids"] };
        var form = Form(email, contact, children);
        var scope = Scope(form, Answers(Answer(email, "anna@example.org"), Answer(contact, "Anna"), InRow(childName, 0, "Ben")));
        var site = MergeTagTestSite.Create();

        var rendered = await site.RenderAsync("{{ field.mail }}|{{ field.contact }}|{{ field.kids.kidName }}|{{ field.kidName }}", scope);

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Is.EqualTo("anna@example.org|Anna|Ben|Ben"));
            Assert.That(site.Service.Check("{{ field.mail.value }} {{ field.kids.kidName.label }}", MergeTagPlace.Mail, MergeTagContext.Text, form), Is.Empty);
        });
    }

    [Test]
    public async Task AFieldTheRulesHid_IsEmpty_AndSoIsEveryChildOfAHiddenGroup()
    {
        var email = Field("email", "email", "Email");
        var childName = Field("text", "childName", "Name");
        var children = Field("repeater", "children", "Children") with { Fields = [childName] };
        var form = Form(email, children);
        var answers = new MergeTagAnswers([Answer(email, "anna@example.org"), InRow(childName, 0, "Ben")], [email.Key, children.Key]);

        var rendered = await MergeTagTestSite.Create().RenderAsync(
            "[{{ field.email }}|{{ field.email.value }}|{{ field.children }}|{{ field.childName }}|{{ field.email.label }}]",
            Scope(form, answers));

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Is.EqualTo("[||||Email]"), "the label is no answer, so it shows");
            Assert.That(rendered.Problems, Is.Empty);
        });
    }

    [Test]
    public async Task AFieldWithoutAnAnswer_IsEmpty_AndALabelFallsBackToTheAlias()
    {
        var email = Field("email", "email");
        var rendered = await MergeTagTestSite.Create().RenderAsync("[{{ field.email }}|{{ field.email.label }}]", Scope(Form(email), Answers()));

        Assert.That(rendered.Text, Is.EqualTo("[|email]"));
    }

    [Test]
    public async Task APathThatNamesNoField_OrNothingOfIt_IsUnknown_WhenCheckedAndWhenRendered()
    {
        var email = Field("email", "email", "Email");
        var name = Field("name", "person", "Person");
        var children = Field("repeater", "children", "Children") with { Fields = [Field("text", "childName", "Name")] };
        var form = Form(email, name, children);
        var site = MergeTagTestSite.Create();
        const string Unknown = "{{ field.nobody }} {{ field.email.first }} {{ field.email.value.more }} {{ field.children.value }} {{ field.children.nobody }}";

        var check = site.Service.Check(Unknown, MergeTagPlace.Mail, MergeTagContext.Text, form);
        var rendered = await site.RenderAsync(Unknown, Scope(form, Answers()));
        var known = site.Service.Check("{{ field.email }} {{ field.email.value }} {{ field.person.first }} {{ field.children.childName.value }} {{ field.children.label }}", MergeTagPlace.Mail, MergeTagContext.Text, form);

        Assert.Multiple(() =>
        {
            Assert.That(check.Select(problem => problem.Code), Is.EqualTo(Enumerable.Repeat(MergeTagProblems.UnknownPath, 5)));
            Assert.That(rendered.Problems.Select(problem => problem.Code), Is.EqualTo(Enumerable.Repeat(MergeTagProblems.UnknownPath, 5)));
            Assert.That(known, Is.Empty);
        });
    }

    [Test]
    public void ThePicker_OffersEachFieldByItsLabel_AChildUnderItsGroup()
    {
        var email = Field("email", "email", "Email");
        var heading = Field("heading", "ignored");
        var children = Field("repeater", "children", "Children") with { Fields = [Field("text", "childName", "Name")] };

        var paths = MergeTagTestSite.Create().Service.Paths(MergeTagPlace.Mail, Form(email, heading, children)).Where(path => path.Path.StartsWith("field.", StringComparison.Ordinal));

        Assert.That(paths, Is.EqualTo(new[] { new MergeTagPathInfo("field.email", "Email"), new MergeTagPathInfo("field.children", "Children"), new MergeTagPathInfo("field.children.childName", "Name") }));
    }

    [Test]
    public async Task ATypeWhoseDisplayThrows_IsLogged_AndShowsAsTheDefaultWould()
    {
        var types = TestFieldTypes.Build(builder => builder.Replace<Core.FieldTypes.BuiltIn.DropdownFieldType, ThrowingDropdown>());
        var site = MergeTagTestSite.Create(types: types);
        var dropdown = Field("dropdown", "fruit", "Fruit") with { Options = Options(("a", "Apple")) };

        var rendered = await site.RenderAsync("{{ field.fruit }}", Scope(Form(dropdown), Answers(Answer(dropdown, "a"))));

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Is.EqualTo("Apple"));
            Assert.That(site.Log.Entries.Single().Message, Does.Contain("'dropdown'").And.Contain(nameof(InvalidOperationException)).And.Not.Contain("secret"));
        });
    }

    /// <summary>A dropdown whose display fails with a message that holds an answer.</summary>
    private sealed class ThrowingDropdown : FormBuilderFieldTypeBase<Core.FieldTypes.BuiltIn.DropdownFieldSettings>
    {
        public ThrowingDropdown()
            : base(new Core.FieldTypes.BuiltIn.DropdownFieldType().Declaration)
        {
        }

        protected override void Display(Core.FieldTypes.BuiltIn.DropdownFieldSettings settings, FieldDisplay display) =>
            throw new InvalidOperationException("secret answer");
    }
}
