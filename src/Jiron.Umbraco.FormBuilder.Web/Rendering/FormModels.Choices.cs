using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Projection;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Web.Rendering.Models;
using Microsoft.AspNetCore.Html;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

// The choice types' controls (ADR-0096): a group of choices in a fieldset with its legend, one box with its own label,
// or a select; each option's input with an id of its own, the answers chosen, and options from a source resolved as
// the options endpoint resolves them.
internal sealed partial class FormModels
{
    // The most lines a list box shows at once.
    private const int MostListLines = 6;

    // What labels a field that holds a value: a fieldset's legend for a group of choices, the box's own label for one
    // box, otherwise a label for its control.
    private static FieldLabelElement LabelElementOf(IFormBuilderFieldType type, ShownSettings settings) => type switch
    {
        RadioFieldType or CheckboxesFieldType or GridFieldType or RankingFieldType or ScaleFieldType or RatingFieldType => FieldLabelElement.Legend,
        NameFieldType or AddressFieldType or SignatureFieldType => FieldLabelElement.Legend,
        YesNoFieldType => IsSwitch(settings) ? FieldLabelElement.Choice : FieldLabelElement.Legend,
        CheckboxFieldType or ConsentFieldType => FieldLabelElement.Choice,
        _ => FieldLabelElement.Label,
    };

    // How a field's label is marked by the form's style. One box marks only a required tick, as it may otherwise stay
    // unticked by nature, and a switch never, as off is an answer; a grid with a required row is not "optional"; a
    // calculation never, as the visitor gives it no answer, and a slider never, as it always holds one.
    private static FieldMark MarkOf(Build build, PublicField field, IFormBuilderFieldType type, FieldLabelElement element, bool isRequired)
    {
        if (element == FieldLabelElement.None || (type is YesNoFieldType && element == FieldLabelElement.Choice) || type is CalculationFieldType or SliderFieldType)
        {
            return FieldMark.None;
        }

        return build.Marker switch
        {
            RequiredMarker.Required when isRequired => FieldMark.Required,
            RequiredMarker.Optional when !isRequired && element != FieldLabelElement.Choice && !field.Rows.Any(row => row.Required) => FieldMark.Optional,
            _ => FieldMark.None,
        };
    }

    private static bool IsSwitch(ShownSettings settings) => settings.Text("style") == "switch";

    // The control of a choice type and the ids of its parts its control names; null for any other type.
    private async Task<(object? Model, IReadOnlyList<string> DescribedBy)?> ChoiceControlAsync(
        Build build, PublicField field, IFormBuilderFieldType type, ShownSettings settings, string id, FieldAnswers answers, CancellationToken cancellationToken)
    {
        switch (type)
        {
            case RadioFieldType:
                return (await ListAsync(build, field, settings, id, answers, several: false, cancellationToken), []);

            case CheckboxesFieldType:
                return (await ListAsync(build, field, settings, id, answers, several: true, cancellationToken), []);

            case DropdownFieldType:
                {
                    var select = await DropdownAsync(build, field, settings, id, answers, cancellationToken);
                    return (select, select.Hint is { } hint ? [hint.Id] : []);
                }

            case CountryFieldType:
                return (Country(build, settings, answers), []);

            case YesNoFieldType when IsSwitch(settings):
                return (new SingleChoiceControlModel
                {
                    Value = YesNoFieldType.Yes,
                    Label = Encoded(build.Text(field.Key, "label", field.Label)),
                    IsChecked = answers.Values.Contains(YesNoFieldType.Yes, StringComparer.Ordinal),
                }, []);

            case YesNoFieldType:
                return (YesNo(build, settings, id, answers), []);

            case CheckboxFieldType:
                return (Box(build, field, settings, answers, wording: null), []);

            case ConsentFieldType:
                return (Box(build, field, settings, answers, build.Render.Shown.Consents.GetValueOrDefault(field.Key) is { } hash ? new HiddenInputModel(ConsentPrefix + (field.Alias ?? string.Empty), hash) : null), []);

            case ScaleFieldType:
                {
                    var from = Math.Clamp(settings.Int("from", new ScaleFieldSettings().From), 0, 1);
                    var to = Math.Clamp(settings.Int("to", new ScaleFieldSettings().To), 5, 10);
                    return (Numbers(settings, id, answers, from, to, "jfb-choices jfb-choices-scale"), []);
                }

            case RatingFieldType:
                {
                    var count = Math.Clamp(settings.Int("count", new RatingFieldSettings().Count), 3, 10);
                    var symbol = settings.Text("symbol") is "heart" or "circle" ? settings.Text("symbol") : "star";
                    return (Numbers(settings, id, answers, 1, count, "jfb-choices jfb-choices-rating jfb-choices-" + symbol), []);
                }

            case GridFieldType:
                return (await GridAsync(build, field, settings, id, answers, cancellationToken), []);

            case RankingFieldType:
                return (await RankingAsync(build, field, settings, id, answers, cancellationToken), []);

            default:
                return null;
        }
    }

    // Radio buttons or checkboxes in the field's layout (the form's when it says inherit), each option's image on a
    // card, and the "Other" choice after them, which posts no value: its text is the answer (ADR-0058).
    private async Task<ChoicesControlModel> ListAsync(Build build, PublicField field, ShownSettings settings, string id, FieldAnswers answers, bool several, CancellationToken cancellationToken)
    {
        var (options, available) = await OptionsAsync(build, field, cancellationToken);
        var layout = settings.Text("layout") switch
        {
            "vertical" => ChoiceLayout.Vertical,
            "horizontal" => ChoiceLayout.Horizontal,
            "cards" => ChoiceLayout.Cards,
            _ => build.Layout,
        };
        var chosen = answers.Values.ToHashSet(StringComparer.Ordinal);
        var choices = new List<ChoiceModel>();
        foreach (var option in options)
        {
            choices.Add(new ChoiceModel
            {
                Id = ChoiceId(id, choices.Count),
                Value = option.Value,
                Label = option.Label,
                Description = option.Description,
                Image = layout == ChoiceLayout.Cards && option.Image is { } image ? await CardImageAsync(image) : null,
                IsChecked = chosen.Contains(option.Value),
            });
        }

        OtherChoiceModel? other = null;
        if (settings.Bool("other", false))
        {
            var text = answers.Part(BuiltInAnswers.OtherPart) is [var given, ..] ? given : string.Empty;
            var choice = new ChoiceModel
            {
                Id = ChoiceId(id, choices.Count),
                Value = string.Empty,
                Label = settings.Text("otherLabel") is { Length: > 0 } label ? label : build.Words.Other,
                IsChecked = text.Length > 0 && (several || answers.Values.IsEmpty),
            };
            other = new OtherChoiceModel(choice, build.Words.OtherText, id + "-" + BuiltInAnswers.OtherPart, (field.Alias ?? string.Empty) + "." + BuiltInAnswers.OtherPart, text);
        }

        return new ChoicesControlModel
        {
            InputType = several ? "checkbox" : "radio",
            CssClass = Classes("jfb-choices", layout switch
            {
                ChoiceLayout.Horizontal => "jfb-choices-horizontal",
                ChoiceLayout.Cards => "jfb-choices-cards",
                _ => null,
            }),
            Choices = choices,
            Other = other,
            Empty = choices.Count == 0 && other is null ? NoOptions(build, available) : null,
        };
    }

    // Yes and no as two buttons, each with the field's label for it or the culture's word.
    private static ChoicesControlModel YesNo(Build build, ShownSettings settings, string id, FieldAnswers answers)
    {
        var culture = build.Render.Culture;
        return new ChoicesControlModel
        {
            InputType = "radio",
            CssClass = "jfb-choices jfb-choices-buttons",
            Choices =
            [
                new ChoiceModel
                {
                    Id = ChoiceId(id, 0),
                    Value = YesNoFieldType.Yes,
                    Label = settings.Text("yesLabel") is { Length: > 0 } yes ? yes : FieldDisplayWords.Yes(culture),
                    IsChecked = answers.Values.Contains(YesNoFieldType.Yes, StringComparer.Ordinal),
                },
                new ChoiceModel
                {
                    Id = ChoiceId(id, 1),
                    Value = YesNoFieldType.No,
                    Label = settings.Text("noLabel") is { Length: > 0 } no ? no : FieldDisplayWords.No(culture),
                    IsChecked = answers.Values.Contains(YesNoFieldType.No, StringComparer.Ordinal),
                },
            ],
        };
    }

    // A scale's or a rating's whole numbers, the words at its ends heard with the first and the last.
    private static ChoicesControlModel Numbers(ShownSettings settings, string id, FieldAnswers answers, int from, int to, string cssClass)
    {
        var low = settings.Text("lowLabel") is { Length: > 0 } lowText ? lowText : null;
        var high = settings.Text("highLabel") is { Length: > 0 } highText ? highText : null;
        var choices = new List<ChoiceModel>();
        for (var number = from; number <= to; number++)
        {
            var value = number.ToString(CultureInfo.InvariantCulture);
            choices.Add(new ChoiceModel
            {
                Id = ChoiceId(id, choices.Count),
                Value = value,
                Label = value,
                HiddenText = number == from && low is not null ? ", " + low : number == to && high is not null ? ", " + high : null,
                IsChecked = answers.Values.Contains(value, StringComparer.Ordinal),
            });
        }

        return new ChoicesControlModel
        {
            InputType = "radio",
            CssClass = cssClass,
            Choices = choices,
            Ends = low is null && high is null ? null : new ScaleEndsModel(low ?? string.Empty, high ?? string.Empty),
        };
    }

    // A dropdown with its first line, or a list box that takes several choices with how to choose them.
    private async Task<SelectControlModel> DropdownAsync(Build build, PublicField field, ShownSettings settings, string id, FieldAnswers answers, CancellationToken cancellationToken)
    {
        var (options, available) = await OptionsAsync(build, field, cancellationToken);
        var chosen = answers.Values.ToHashSet(StringComparer.Ordinal);
        var multiple = settings.Bool("multiple", false);
        return new SelectControlModel
        {
            Multiple = multiple,
            Size = multiple ? Math.Clamp(options.Count, 2, MostListLines) : null,
            Prompt = multiple ? null : settings.Text("prompt") is { Length: > 0 } prompt ? prompt : build.Words.Choose,
            Options = [.. options.Select(option => new SelectOptionModel(option.Value, option.Label, chosen.Contains(option.Value)))],
            Hint = multiple && options.Count > 0 ? new HintModel(build.Words.SeveralHint, id + "-hint") : null,
            Empty = options.Count == 0 ? NoOptions(build, available) : null,
        };
    }

    // A country: the allowed ones, the ones shown first at the top; the field's default country while nothing is answered
    // on a first render.
    private static SelectControlModel Country(Build build, ShownSettings settings, FieldAnswers answers)
    {
        var allowed = settings.Texts("allowed").Where(BuiltInChecks.CountryCodes.Contains).ToList();
        var offered = allowed.Count > 0 ? allowed : [.. BuiltInChecks.CountryCodes];
        var top = settings.Texts("top").Where(offered.Contains).Distinct(StringComparer.Ordinal).ToList();
        IReadOnlyCollection<string> chosen = !answers.Values.IsEmpty || build.Post is not null ? answers.Values : settings.Text("default") is { } country ? [country] : [];
        return Countries(build, offered, top, chosen);
    }

    // Countries to choose from, each named in the culture's language: the ones shown first, then the others by their
    // names in the culture's order, the chosen ones selected.
    private static SelectControlModel Countries(Build build, IEnumerable<string> offered, IReadOnlyList<string> top, IReadOnlyCollection<string> chosen)
    {
        var culture = build.Render.Culture;
        var order = StringComparer.Create(build.Culture, ignoreCase: true);
        var codes = top.Concat(offered.Except(top, StringComparer.Ordinal).OrderBy(code => CountryNames.Name(code, culture), order).ThenBy(code => code, StringComparer.Ordinal));
        return new SelectControlModel
        {
            Prompt = build.Words.ChooseCountry,
            Options = [.. codes.Select(code => new SelectOptionModel(code, CountryNames.Name(code, culture), chosen.Contains(code, StringComparer.Ordinal)))],
        };
    }

    // One box: a single checkbox or a consent, labelled by its text when it has one, otherwise by the field's label.
    private static SingleChoiceControlModel Box(Build build, PublicField field, ShownSettings settings, FieldAnswers answers, HiddenInputModel? wording) => new()
    {
        Value = FieldAnswerDefaults.Ticked,
        Label = settings.Text("text") is { Length: > 0 } html ? new HtmlString(html) : Encoded(build.Text(field.Key, "label", field.Label)),
        IsChecked = answers.Values.Contains(FieldAnswerDefaults.Ticked, StringComparer.Ordinal),
        Wording = wording,
    };

    // A grid's rows, each a fieldset of a choice per column: the scale's numbers, or the field's options. A row's
    // inputs are named by the row's key; the first row's first input carries the field's id.
    private async Task<GridControlModel> GridAsync(Build build, PublicField field, ShownSettings settings, string id, FieldAnswers answers, CancellationToken cancellationToken)
    {
        IReadOnlyList<ShownOption> columns;
        var available = true;
        if (settings.Child("scale") is { } scale)
        {
            var from = Math.Clamp(scale.Int("from", new GridScale().From), 0, 1);
            var to = Math.Clamp(scale.Int("to", new GridScale().To), 2, 10);
            columns = [.. Enumerable.Range(from, to - from + 1).Select(number => number.ToString(CultureInfo.InvariantCulture)).Select(number => new ShownOption(number, number, null, null))];
        }
        else
        {
            (columns, available) = await OptionsAsync(build, field, cancellationToken);
        }

        var multiple = settings.Bool("multiple", false);
        var gridRequired = build.Required.Contains(Id(field.Key));
        var rows = new List<GridRowModel>();
        if (columns.Count > 0)
        {
            foreach (var row in field.Rows)
            {
                var index = rows.Count;
                var part = GridFieldType.RowPart(row.Key);
                var chosen = answers.Part(part).ToHashSet(StringComparer.Ordinal);
                var marked = build.Marker == RequiredMarker.Required && !gridRequired && row.Required;
                build.MarksRequired |= marked;
                rows.Add(new GridRowModel
                {
                    Key = row.Key,
                    Label = Text(row.Label) ?? string.Format(build.Culture, build.Words.Row, index + 1),
                    Name = (field.Alias ?? string.Empty) + "." + part,
                    IsRequired = gridRequired || row.Required,
                    Mark = marked ? FieldMark.Required : FieldMark.None,
                    MarkText = marked ? "*" : string.Empty,
                    Choices =
                    [
                        .. columns.Select((column, at) => new ChoiceModel
                        {
                            Id = index == 0 && at == 0 ? id : id + "-" + (index + 1).ToString(CultureInfo.InvariantCulture) + "-" + (at + 1).ToString(CultureInfo.InvariantCulture),
                            Value = column.Value,
                            Label = column.Label,
                            IsChecked = chosen.Contains(column.Value),
                        }),
                    ],
                });
            }
        }

        return new GridControlModel
        {
            InputType = multiple ? "checkbox" : "radio",
            Columns = [.. columns.Select(column => column.Label)],
            Rows = rows,
            Empty = rows.Count == 0 ? NoOptions(build, available) : null,
        };
    }

    // A ranking's options in the visitor's order (as positions posted without JavaScript, or in order), then the others
    // in their own order; each of the first few it asks for posts its position, and each moves up or down with a submit
    // button the no-JS post reads (6.2g).
    private async Task<RankingControlModel> RankingAsync(Build build, PublicField field, ShownSettings settings, string id, FieldAnswers answers, CancellationToken cancellationToken)
    {
        var (options, available) = await OptionsAsync(build, field, cancellationToken);
        var alias = field.Alias ?? string.Empty;
        var top = settings.Int("top", 0);
        var ranked = top > 0 ? Math.Min(top, options.Count) : options.Count;
        var given = answers.Parts.IsEmpty
            ? answers.Values.ToList()
            : [.. answers.Parts
                .Select(part => (Value: part.Key, Position: part.Value is [var text] && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var position) ? position : int.MaxValue))
                .OrderBy(part => part.Position)
                .ThenBy(part => part.Value, StringComparer.Ordinal)
                .Select(part => part.Value)];
        var byValue = options.GroupBy(option => option.Value, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var order = given.Where(byValue.ContainsKey).Distinct(StringComparer.Ordinal).Select(value => byValue[value]).ToList();
        var placed = order.Select(option => option.Value).ToHashSet(StringComparer.Ordinal);
        order.AddRange([.. options.Where(option => placed.Add(option.Value))]);
        var items = new List<RankingItemModel>();
        foreach (var option in order)
        {
            var at = items.Count;
            var n = (at + 1).ToString(CultureInfo.InvariantCulture);
            items.Add(new RankingItemModel
            {
                Label = option.Label,
                Position = at < ranked ? at + 1 : null,
                Input = at < ranked ? new HiddenInputModel(alias + "." + option.Value, n) : null,
                Up = new RankingMoveModel(string.Format(build.Culture, build.Words.MoveUp, option.Label), "up:" + alias + ":" + n, at == 0, at == 0 ? id : null),
                Down = new RankingMoveModel(string.Format(build.Culture, build.Words.MoveDown, option.Label), "down:" + alias + ":" + n, at == order.Count - 1, null),
            });
        }

        return new RankingControlModel { Items = items, Empty = items.Count == 0 ? NoOptions(build, available) : null };
    }

    // A field's options as the page offers them: its own as the form endpoint shows them in the culture; or, for
    // options from a list or another source and for options a parent filters, those the options endpoint answers for
    // the parent's answers on the page (ADR-0081, ADR-0086), a parent a rule hides answering nothing. A label that is
    // blank shows the value, as the endpoint shows it.
    private async Task<(IReadOnlyList<ShownOption> Options, bool IsAvailable)> OptionsAsync(Build build, PublicField field, CancellationToken cancellationToken)
    {
        // Publishing gives each type that asks here its options; a grid without them has a scale and does not ask.
        var options = field.Options!;
        if (!options.FromSource && options.Parent is null)
        {
            return ([.. options.Items.Select(item => new ShownOption(item.Value, Shown(item.Label?.Default, item.Value), Shown(item.Description?.Default, null), item.Image))], true);
        }

        var render = build.Render;
        var resolved = await cascading.GetAsync(render.Form.Published.Definition, build.Definition(field.Key), key => build.ShownAnswers(key), render.Culture, render.Shown.Scope.Page, cancellationToken);
        return ([.. resolved.Options.Select(option => new ShownOption(option.Value, Shown(option.Label, option.Value), Shown(option.Description, null), option.Image))], resolved.IsAvailable);
    }

    // An option's card image, when its media item has an address.
    private async Task<ChoiceImageModel?> CardImageAsync(Guid key) =>
        await MediaAsync(key) is { Url: { } url } found ? new ChoiceImageModel(url, found.Width, found.Height) : null;

    private static string NoOptions(Build build, bool available) => available ? build.Words.NoChoices : build.Words.ChoicesUnavailable;

    private static string ChoiceId(string id, int index) => index == 0 ? id : id + "-" + (index + 1).ToString(CultureInfo.InvariantCulture);

    private static IHtmlContent Encoded(string? text) => new HtmlContentBuilder().Append(text ?? string.Empty);

    [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(fallback))]
    private static string? Shown(string? text, string? fallback) => string.IsNullOrWhiteSpace(text) ? fallback : text;

    // An option as the page shows it.
    private sealed record ShownOption(string Value, string Label, string? Description, Guid? Image);
}
