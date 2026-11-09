using System.Text;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;

/// <summary>
/// Builds random, fully populated definitions from a seed, for property-style tests: every property gets a value
/// somewhere, texts mix scripts, astral characters, quotes, backslashes and control characters, and conditions nest.
/// What it builds is within the save limits, and never something the canonical form leaves out (an empty text, an
/// empty list default, empty settings), so a lossless round trip gives back the same model.
/// </summary>
internal sealed class DefinitionGenerator
{
    /// <summary>The field types of section 4.3.2, plus one a site might add.</summary>
    public static readonly string[] Types =
    [
        "text", "textarea", "email", "phone", "url", "number", "date", "time", "datetime", "rating", "scale", "slider",
        "calculation", "dropdown", "radio", "checkboxes", "grid", "ranking", "checkbox", "yesno", "consent", "country",
        "name", "address", "file", "signature", "repeater", "hidden", "heading", "paragraph", "divider", "image", "captcha",
        "acme.colourPicker",
    ];

    private static readonly string[] Pieces =
    [
        "a", "b", "z", "Q", "0", "7", " ", "-", "_", ".", "\"", "\\", "/", "<", ">", "&", "'", "{{ field.name }}", "#",
        C(0xE9), C(0xDF), C(0x416), C(0x3A3), C(0x4E2D), C(0x30AB), C(0x1F39F), C(0x10000), "\t", C(0x1), C(0x7F),
        C(0xA0), C(0xFEFF),
    ];

    private static readonly string[] LinePieces = ["\n", "\r\n", C(0x2028), C(0x85)];

    private static readonly string[] Cultures = ["de", "de-CH", "fr", "sr-Latn-RS", "zh-Hans", "EN-gb", "it"];

    private static readonly string[] Operators = ["equals", "notEquals", "isOneOf", "between", "isEmpty", "matches", "countAtLeast", "bogus"];

    private static readonly string[] Contexts = ["culture", "memberSignedIn", "memberGroup", "today", "query.ref", "nonsense"];

    private readonly Random _random;
    private readonly List<Guid> _keys = [];

    public DefinitionGenerator(int seed) => _random = new Random(seed);

    // A character by its code point, so the source stays ASCII (scripts/check-text.mjs).
    private static string C(int codePoint) => char.ConvertFromUtf32(codePoint);

    public FormDefinition Next()
    {
        _keys.Clear();
        var pages = Many(1, 3, Page);
        return new FormDefinition
        {
            Key = NewKey(),
            Settings = Settings(),
            Pages = pages,
            ValidationRules = Many(0, 2, ValidationRule),
            Actions = Many(0, 3, Action),
            Outcomes = Many(0, 3, Outcome),
        };
    }

    private bool Chance(int percent = 50) => _random.Next(100) < percent;

    private T Pick<T>(IReadOnlyList<T> items) => items[_random.Next(items.Count)];

    private TEnum Word<TEnum>()
        where TEnum : struct, Enum => Pick(Enum.GetValues<TEnum>());

    private List<T> Many<T>(int min, int max, Func<T> make) => Enumerable.Range(0, _random.Next(min, max + 1)).Select(_ => make()).ToList();

    private Guid NewKey()
    {
        var bytes = new byte[16];
        _random.NextBytes(bytes);
        var key = new Guid(bytes);
        _keys.Add(key);
        return key;
    }

    // A key of an element made earlier, or of nothing at all: the save checks do not resolve references.
    private Guid Reference()
    {
        if (_keys.Count > 0 && Chance(80))
        {
            return Pick(_keys);
        }

        var bytes = new byte[16];
        _random.NextBytes(bytes);
        return new Guid(bytes);
    }

    private string String(int max, bool singleLine = true)
    {
        var text = new StringBuilder();
        var length = _random.Next(0, Math.Min(max, 40) + 1);
        while (text.Length < length)
        {
            var piece = !singleLine && Chance(10) ? Pick(LinePieces) : Pick(Pieces);
            if (text.Length + piece.Length > max)
            {
                break;
            }

            text.Append(piece);
        }

        return text.ToString();
    }

    private string Identifier() => "f" + _random.Next(1_000_000).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private LocalizedText? Text(bool singleLine = true) => Chance(80) ? new LocalizedText(Texts(singleLine ? 500 : 2000, singleLine)) : null;

    private LocalizedHtml? Html() => Chance(80) ? new LocalizedHtml(Texts(50000, singleLine: false)) : null;

    private List<KeyValuePair<string, string>> Texts(int max, bool singleLine)
    {
        var texts = new List<KeyValuePair<string, string>> { new(string.Empty, String(max, singleLine)) };
        foreach (var culture in Cultures.OrderBy(_ => _random.Next()).Take(_random.Next(0, 4)))
        {
            texts.Add(new KeyValuePair<string, string>(culture, String(max, singleLine)));
        }

        return texts;
    }

    private SettingsObject? Extension() => Chance(80) ? SettingsObject.Parse(SettingsNode(0).ToJsonString()) : null;

    private JsonObject SettingsNode(int depth)
    {
        var node = new JsonObject();
        foreach (var name in new[] { "a", "maxLength", "label", "list", "z_1", C(0xDC) + "ber", "with space" }.OrderBy(_ => _random.Next()).Take(_random.Next(1, 5)))
        {
            node[name] = SettingsValue(depth);
        }

        return node;
    }

    private JsonNode SettingsValue(int depth) => (depth < 2 ? _random.Next(6) : _random.Next(3)) switch
    {
        0 => JsonValue.Create(String(60, singleLine: false)),
        1 => JsonValue.Create(_random.NextInt64(-999_999_999_999_999, 1_000_000_000_000_000)),
        2 => JsonValue.Create(Chance()),
        3 => SettingsNode(depth + 1),
        4 => new JsonArray(Enumerable.Range(0, _random.Next(1, 4)).Select(_ => SettingsValue(depth + 1)).ToArray()),
        _ => new JsonObject { ["text"] = new JsonObject { [""] = String(30) } },
    };

    private DateTime? Instant() =>
        Chance(70) ? DateTime.SpecifyKind(DateTime.UnixEpoch.AddSeconds(_random.NextInt64(0, 4_102_444_800)), DateTimeKind.Utc) : null;

    private int? Maybe(int min, int max) => Chance(70) ? _random.Next(min, max + 1) : null;

    private FormSettings Settings()
    {
        var store = Word<EntryStorage>();
        return new FormSettings
        {
            Submission = new FormSubmissionSettings
            {
                SubmitLabel = Text(),
                NextLabel = Text(),
                BackLabel = Text(),
                SummaryPage = Chance(),
                Progress = Word<ProgressIndicator>(),
                Store = store,
                StoreDays = store == EntryStorage.Days ? _random.Next(1, DefinitionSaveChecks.MaxDays + 1) : null,
                Approval = Word<ApprovalMode>(),
                Verification = Chance() ? new EmailVerification { EmailField = Reference(), Days = _random.Next(1, 91) } : null,
                Resume = Chance() ? new SaveAndResume { Days = _random.Next(1, 366) } : null,
                EntryTitle = Chance() ? String(500) : null,
                EntryReference = Chance() ? new EntryReferenceFormat { Prefix = Chance() ? String(20) : null, IncludeYear = Chance(), Digits = _random.Next(1, 11) } : null,
            },
            Availability = new FormAvailabilitySettings
            {
                OpensAt = Instant(),
                ClosesAt = Instant(),
                MaxEntries = Maybe(1, int.MaxValue - 1),
                ClosedMessage = Html(),
                Access = Word<MemberAccess>(),
                MemberGroups = Many(0, 3, Reference),
                OnePerMember = Chance(),
            },
            Protection = new FormProtectionSettings
            {
                Level = Word<ProtectionLevel>(),
                Captcha = Chance() ? new CaptchaChoice { Provider = String(40), Settings = Extension() } : null,
                ContentRules = Word<ContentRuleLevel>(),
            },
            Privacy = new FormPrivacySettings
            {
                RetentionDays = new RetentionDaysByState { Submitted = Maybe(1, DefinitionSaveChecks.MaxDays), Approved = Maybe(1, DefinitionSaveChecks.MaxDays), Rejected = Maybe(1, DefinitionSaveChecks.MaxDays), Spam = Maybe(1, DefinitionSaveChecks.MaxDays) },
                IP = Word<IPHandling>(),
                Insights = Chance(),
            },
            Appearance = new FormAppearanceSettings
            {
                Theme = Chance() ? String(64) : null,
                RequiredMarker = Word<RequiredMarker>(),
                ValidationSummary = Chance(),
                ChoiceLayout = Word<ChoiceLayout>(),
                CssClass = Chance() ? String(80) : null,
            },
            Developer = new FormDeveloperSettings { Headless = Chance(), QueryValues = Many(0, 3, () => String(30)), ContextValues = Many(0, 3, () => String(30)) },
        };
    }

    private FormPage Page() => new()
    {
        Key = NewKey(),
        Title = Text(),
        Description = Html(),
        Rules = Many(0, 2, Rule),
        Groups = Many(1, 3, Group),
    };

    private FormGroup Group()
    {
        var columns = _random.Next(1, 5);
        return new FormGroup
        {
            Key = NewKey(),
            Title = Text(),
            Description = Html(),
            Columns = columns,
            Style = Word<GroupStyle>(),
            Rules = Many(0, 2, Rule),
            Fields = Many(0, 4, () => Field(columns, childOf: false)),
        };
    }

    private FormField Field(int columns, bool childOf)
    {
        var type = Pick(Types);
        return new FormField
        {
            Key = NewKey(),
            Type = type,
            Alias = Chance(85) ? Identifier() : null,
            FormerAliases = Many(0, 2, Identifier),
            Span = _random.Next(1, columns + 1),
            Label = Text(),
            HideLabel = Chance(),
            Help = Text(singleLine: false),
            Placeholder = Text(),
            Required = Chance(),
            Messages = Chance() ? Messages() : new(),
            DefaultValue = _random.Next(3) switch
            {
                0 => null,
                1 => FieldDefaultValue.Of(String(200, singleLine: false)),
                _ => FieldDefaultValue.OfList(Many(1, 3, () => String(50))),
            },
            Prefill = Chance() ? new FieldPrefill { Query = Chance() ? Identifier() : null, Member = Chance() ? Identifier() : null, Context = Chance() ? Identifier() : null, MemberMode = Word<PrefillMemberMode>() } : null,
            Sensitive = Chance(),
            IncludeInEmails = Chance(),
            Keep = Word<FieldKeep>(),
            Autocomplete = Chance() ? String(30) : null,
            CssClass = Chance() ? String(30) : null,
            Validation = Many(0, 3, Check),
            Rules = Many(0, 2, Rule),
            Options = Chance(40) ? Options() : null,
            Rows = Chance(25) ? Many(1, 3, Row) : [],
            Fields = !childOf && Chance(25) ? Many(1, 3, () => Field(columns, childOf: true)) : [],
            Settings = Extension(),
        };
    }

    // At least one message, because a field without any writes no messages object.
    private FieldMessages Messages() => new()
    {
        Required = new LocalizedText(Texts(500, singleLine: true)),
        Invalid = Text(),
        MinLength = Text(),
        MaxLength = Text(),
        MinWords = Text(),
        MaxWords = Text(),
        Min = Text(),
        Max = Text(),
        Step = Text(),
        DateWindow = Text(),
        Selections = Text(),
        FileType = Text(),
        FileSize = Text(),
        FileCount = Text(),
        Full = Text(),
        Confirmation = Text(),
        Digits = Text(),
        RowCount = Text(),
    };

    private FieldCheck Check() => _random.Next(3) switch
    {
        0 => new PatternCheck { Pattern = String(100, singleLine: false), Message = Text() },
        1 => new MatchesFieldCheck { Field = Reference(), Message = Text() },
        _ => new UniquePerFormCheck { Message = Text() },
    };

    private FieldOptions Options() => new()
    {
        Source = Word<OptionSource>(),
        Items = Many(0, 5, Option),
        OptionList = Chance() ? Reference() : null,
        Config = Extension(),
        Parent = Chance() ? Reference() : null,
    };

    private FieldOption Option() => new()
    {
        Value = "v" + String(150),
        Label = Text(),
        Description = Text(singleLine: false),
        Image = Chance() ? Reference() : null,
        Places = Maybe(0, 1_000_000),
        ParentValues = Many(0, 2, () => "p" + String(100)),
    };

    private GridRow Row() => new() { Key = NewKey(), Label = Text(), Required = Chance() };

    private FormRule Rule() => new()
    {
        Key = NewKey(),
        Effect = Word<RuleEffect>(),
        Enabled = Chance(),
        Match = Word<RuleMatch>(),
        Conditions = Many(0, 3, () => Item(0)),
    };

    private RuleItem Item(int depth) => depth < 3 && Chance(25)
        ? new RuleGroup { Match = Word<RuleMatch>(), Conditions = Many(0, 3, () => Item(depth + 1)) }
        : Condition();

    private RuleCondition Condition()
    {
        var onField = Chance(70);
        return new RuleCondition
        {
            Field = onField || Chance(10) ? Reference() : null,
            Context = !onField || Chance(10) ? Pick(Contexts) : null,
            Operator = Pick(Operators),
            Value = _random.Next(5) switch
            {
                0 => null,
                1 => new ConditionOperand.List(Many(1, 3, OneOperand)),
                _ => OneOperand(),
            },
        };
    }

    private ConditionOperand OneOperand() => _random.Next(3) switch
    {
        0 => new ConditionOperand.Text(String(100, singleLine: false)),
        1 => new ConditionOperand.Field(Reference()),
        _ => new ConditionOperand.Today(Pick(new[] { string.Empty, "+2d", "-1m+1d", "+1y", "bad" })),
    };

    private FormValidationRule ValidationRule() => new()
    {
        Key = NewKey(),
        Enabled = Chance(),
        Field = Reference(),
        Message = Text(),
        Match = Word<RuleMatch>(),
        Conditions = Many(0, 3, () => Item(0)),
    };

    private FormAction Action() => new()
    {
        Key = NewKey(),
        Type = Pick(new[] { "email", "webhook", "slack", "acme.crm" }),
        Name = Chance() ? String(80) : null,
        Enabled = Chance(),
        Trigger = Word<ActionTrigger>(),
        StopOnFailure = Chance(),
        Rules = Many(0, 1, Rule),
        Settings = Extension(),
    };

    private FormOutcome Outcome() => new()
    {
        Key = NewKey(),
        Kind = Word<OutcomeKind>(),
        Message = Html(),
        ShowAnswers = Chance(),
        Redirect = Chance() ? new OutcomeRedirect { Content = Chance() ? Reference() : null, Url = Chance() ? "/" + String(200) : null, DelaySeconds = _random.Next(0, 601) } : null,
        Rules = Many(0, 1, Rule),
    };
}
