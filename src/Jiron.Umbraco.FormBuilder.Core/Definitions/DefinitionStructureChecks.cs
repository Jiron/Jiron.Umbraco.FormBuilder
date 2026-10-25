using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Core.Definitions;

/// <summary>
/// The structural validation (ADR-0002, ADR-0035): what publishing refuses on top of the save checks, because the
/// parts of the definition do not fit together. Field types and their properties, aliases, option values, references,
/// rules and their conditions, cycles and redirects. A draft keeps every one of these problems and the form check
/// lists them; only a published version is held to them. It runs on a definition the save checks accept.
/// </summary>
internal sealed partial class DefinitionStructureChecks
{
    /// <summary>The longest alias: a lowercase letter, then up to 63 letters and digits.</summary>
    public const int MaxAliasLength = 64;

    /// <summary>The longest name of an allowed query value or a context value.</summary>
    public const int MaxNameLength = 64;

    /// <summary>The aliases no field may take, in any letter case (section 4.3.1).</summary>
    public static readonly FrozenSet<string> ReservedAliases =
        new[] { "form", "entry", "page", "member", "query", "context", "site", "date", "ufprt" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private readonly FieldTypeRegistry _types;
    private readonly DefinitionProblems _problems = new();

    // Every field, repeatable group child and grid row by key; the first of two with one key wins (a save refuses both).
    private readonly Dictionary<Guid, Entry> _fields = [];

    // What conditions can look at: the fields and rows whose answers rules compare, by key, with their kinds.
    private readonly Dictionary<string, FieldKind> _kinds = new(StringComparer.Ordinal);

    // What the visitor's browser can look at as well: the same, without the fields whose value the server sets.
    private readonly Dictionary<string, FieldKind> _visitorKinds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _queryValues = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Node> _nodes = [];
    private readonly List<(Guid Field, Guid Parent, string Path)> _parents = [];

    // The one-per-form types the form has a field of so far, by alias: each type counts on its own.
    private readonly HashSet<string> _onePerForm = new(StringComparer.Ordinal);
    private int _cursor;

    private DefinitionStructureChecks(FieldTypeRegistry types) => _types = types;

    /// <summary>Every structural problem of the definition, at most <see cref="DefinitionProblems.Max"/>.</summary>
    /// <param name="definition">A definition the save checks accept.</param>
    /// <param name="types">The field types the site has.</param>
    public static IReadOnlyList<DefinitionProblem> Check(FormDefinition definition, FieldTypeRegistry types)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(types);
        var checks = new DefinitionStructureChecks(types);
        checks.Index(definition);
        checks.CheckDefinition(definition);
        checks.CheckCycles();
        return checks._problems.ToReadOnlyList();
    }

    private static string P(string path, string name) => JsonPaths.Property(path, name);

    private static string I(string path, int index) => JsonPaths.Index(path, index);

    private static string KeyText(Guid key) => key.ToString("D");

    private static string Name(FormField field) => field.Alias is { } alias ? $"field '{alias}'" : $"field {field.Key}";

    /// <summary>A lowercase ASCII letter, then ASCII letters and digits, at most 64 characters (section 4.3.1).</summary>
    internal static bool IsAlias(string alias) =>
        alias.Length is >= 1 and <= MaxAliasLength && char.IsAsciiLetterLower(alias[0]) && alias.All(char.IsAsciiLetterOrDigit);

    /// <summary>What a merge tag can write after <c>query.</c> or <c>context.</c>: letters, digits, <c>_</c> and <c>-</c>.</summary>
    private static bool IsName(string name) =>
        name.Length is >= 1 and <= MaxNameLength && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');

    private void Fail(string code, string path, string message) => _problems.Add(code, path, message);

    // The first pass: every element that can be shown or hidden gets a node, in document order, and every field, child
    // and row is found by its key, so references may point forward.
    private void Index(FormDefinition definition)
    {
        _queryValues.UnionWith(definition.Settings.Developer.QueryValues);
        var pagesPath = P(JsonPaths.Root, "pages");
        for (var p = 0; p < definition.Pages.Count; p++)
        {
            var pagePath = I(pagesPath, p);
            var page = AddNode(pagePath, $"page {p + 1}", container: null);
            var groupsPath = P(pagePath, "groups");
            for (var g = 0; g < definition.Pages[p].Groups.Count; g++)
            {
                var group = definition.Pages[p].Groups[g];
                var groupPath = I(groupsPath, g);
                var node = AddNode(groupPath, $"group {group.Key}", page);
                IndexFields(group.Fields, P(groupPath, "fields"), node, inRepeater: false);
            }
        }
    }

    private void IndexFields(IReadOnlyList<FormField> fields, string path, int container, bool inRepeater)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            var fieldPath = I(path, i);
            var traits = _types.Find(field.Type);
            var node = AddNode(fieldPath, Name(field), container);
            if (_fields.TryAdd(field.Key, new Entry(field, traits, node, inRepeater, Row: null)) && traits?.KindOf(field) is { } kind)
            {
                _kinds[KeyText(field.Key)] = kind;
                if (traits is { ServerOnlyDefault: false })
                {
                    _visitorKinds[KeyText(field.Key)] = kind;
                }
            }

            // The rows of a field whose type the site lacks are found too, so that references to them wait for the type.
            if (traits is null || traits.Supports(FieldFeatures.Rows))
            {
                foreach (var row in field.Rows)
                {
                    if (_fields.TryAdd(row.Key, new Entry(field, traits, node, inRepeater, row)) && traits is not null)
                    {
                        _kinds[KeyText(row.Key)] = _visitorKinds[KeyText(row.Key)] = traits.RowKindOf(field);
                    }
                }
            }

            IndexFields(field.Fields, P(fieldPath, "fields"), node, inRepeater: true);
        }
    }

    private int AddNode(string path, string description, int? container)
    {
        var node = _nodes.Count;
        _nodes.Add(new Node(path, description));

        // Whether an element is shown depends on whether what contains it is shown (hidden means gone).
        if (container is { } outer)
        {
            _nodes[node].Edges.Add(outer);
        }

        return node;
    }

    // The second pass walks the pages, groups and fields in the same order as the first, taking their nodes in turn.
    private int NextNode(string path)
    {
        var node = _cursor++;
        Debug.Assert(_nodes[node].Path == path, "Both passes walk the definition in the same order.");
        return node;
    }

    private void CheckDefinition(FormDefinition definition)
    {
        CheckSettings(definition.Settings);
        var pagesPath = P(JsonPaths.Root, "pages");
        if (definition.Pages.Count == 0)
        {
            Fail(DefinitionErrors.PartMissing, pagesPath, "A form has at least one page.");
        }

        for (var p = 0; p < definition.Pages.Count; p++)
        {
            var page = definition.Pages[p];
            var pagePath = I(pagesPath, p);
            var pageNode = NextNode(pagePath);
            Rules(page.Rules, P(pagePath, "rules"), Visibility, "A page", pageNode);
            var groupsPath = P(pagePath, "groups");
            if (page.Groups.Count == 0)
            {
                Fail(DefinitionErrors.PartMissing, groupsPath, "A page has at least one group.");
            }

            for (var g = 0; g < page.Groups.Count; g++)
            {
                var group = page.Groups[g];
                var groupPath = I(groupsPath, g);
                var groupNode = NextNode(groupPath);
                Rules(group.Rules, P(groupPath, "rules"), Visibility, "A group", groupNode);
                CheckFields(group.Fields, P(groupPath, "fields"), inRepeater: false);
            }
        }

        CheckValidationRules(definition.ValidationRules);
        CheckActions(definition.Actions);
        CheckOutcomes(definition.Outcomes);
    }

    private void CheckSettings(FormSettings settings)
    {
        var path = P(JsonPaths.Root, "settings");
        if (settings.Submission.Verification is { } verification)
        {
            var fieldPath = P(P(P(path, "submission"), "verification"), "emailField");
            if (!_fields.TryGetValue(verification.EmailField, out var entry) || !entry.Resolves)
            {
                Fail(DefinitionErrors.UnknownField, fieldPath, "Email verification names no field of the form.");
            }
            else if (!entry.Unknown && (entry.Row is not null || entry.Field.Type != "email" || entry.InRepeater))
            {
                Fail(DefinitionErrors.InvalidReference, fieldPath, "Email verification uses an email field outside repeatable groups.");
            }
        }

        var availability = settings.Availability;
        var groupsPath = P(P(path, "availability"), "memberGroups");
        if (availability.Access == MemberAccess.Groups && availability.MemberGroups.Count == 0)
        {
            Fail(DefinitionErrors.PartMissing, groupsPath, "A form for some member groups names at least one group.");
        }
        else if (availability.Access != MemberAccess.Groups && availability.MemberGroups.Count > 0)
        {
            Fail(DefinitionErrors.PropertyNotSupported, groupsPath, "Member groups belong only to a form for some member groups.");
        }

        var protection = settings.Protection;
        var captchaPath = P(P(path, "protection"), "captcha");
        if (protection.Level == ProtectionLevel.Captcha && protection.Captcha is null)
        {
            Fail(DefinitionErrors.PartMissing, captchaPath, "A form protected by a captcha names the captcha's provider.");
        }
        else if (protection.Level != ProtectionLevel.Captcha && protection.Captcha is not null)
        {
            Fail(DefinitionErrors.PropertyNotSupported, captchaPath, "A captcha provider belongs only to the captcha level.");
        }

        var developerPath = P(path, "developer");
        Names(settings.Developer.QueryValues, P(developerPath, "queryValues"));
        Names(settings.Developer.ContextValues, P(developerPath, "contextValues"));
    }

    private void Names(IReadOnlyList<string> names, string path)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < names.Count; i++)
        {
            if (!IsName(names[i]))
            {
                Fail(DefinitionErrors.NameInvalid, I(path, i), "A name has 1 to 64 letters, digits, hyphens and underscores, as merge tags write it.");
            }
            else if (!seen.Add(names[i]))
            {
                Fail(DefinitionErrors.NameDuplicate, I(path, i), $"The name '{names[i]}' is already in the list (letter case does not count).");
            }
        }
    }

    private void CheckFields(IReadOnlyList<FormField> fields, string path, bool inRepeater)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            var fieldPath = I(path, i);
            var node = NextNode(fieldPath);
            var traits = _types.Find(field.Type);
            if (traits is null)
            {
                // Until the field has a type the site knows, only what needs no type is checked.
                Fail(DefinitionErrors.UnknownType, P(fieldPath, "type"), $"The site has no field type '{field.Type}'.");
                Aliases(field, fieldPath);
                Rules(field.Rules, P(fieldPath, "rules"), AnyFieldEffect, "A field", node);
            }
            else
            {
                CheckField(field, traits, fieldPath, node, inRepeater);
            }

            CheckFields(field.Fields, P(fieldPath, "fields"), inRepeater: true);
        }
    }

    private void CheckField(FormField field, FieldTypeTraits traits, string path, int node, bool inRepeater)
    {
        var what = $"A {traits.Alias} field";
        if (inRepeater && (traits.OnePerForm || traits.Supports(FieldFeatures.ChildFields)))
        {
            Fail(DefinitionErrors.FieldNotAllowed, P(path, "type"), $"{what} cannot be inside a repeatable group.");
        }

        if (traits.OnePerForm && !_onePerForm.Add(traits.Alias))
        {
            Fail(DefinitionErrors.FieldNotAllowed, P(path, "type"), $"A form has at most one {traits.Alias} field.");
        }

        Support(field, traits, path);
        if (traits.HoldsValue)
        {
            if (field.Alias is null)
            {
                Fail(DefinitionErrors.AliasInvalid, P(path, "alias"), $"{what} holds a value, so it has an alias.");
            }

            Aliases(field, path);
        }

        CheckOptions(field, traits, path);
        CheckValidation(field, traits, path);
        if (traits.Supports(FieldFeatures.Prefill) && field.Prefill?.Query is { } query && !_queryValues.Contains(query))
        {
            Fail(DefinitionErrors.InvalidReference, P(P(path, "prefill"), "query"), $"The form does not allow the query value '{query}' (settings.developer.queryValues).");
        }

        ImmutableArray<RuleEffect> effects = (traits.Supports(FieldFeatures.VisibilityRules), traits.Supports(FieldFeatures.Required)) switch
        {
            (true, true) => AnyFieldEffect,
            (true, false) => Visibility,
            (false, true) => [RuleEffect.Require],
            _ => [],
        };
        Rules(field.Rules, P(path, "rules"), effects, what, node);

        if (traits.Supports(FieldFeatures.Rows) && field.Rows.Count == 0)
        {
            Fail(DefinitionErrors.PartMissing, P(path, "rows"), $"{what} has at least one row.");
        }

        if (traits.Supports(FieldFeatures.ChildFields) && field.Fields.Count == 0)
        {
            Fail(DefinitionErrors.PartMissing, P(path, "fields"), $"{what} has at least one field.");
        }
    }

    // A property the type does not support keeps its default (ADR-0034).
    private void Support(FormField field, FieldTypeTraits traits, string path)
    {
        void Unsupported(string property, string reason) =>
            Fail(DefinitionErrors.PropertyNotSupported, P(path, property), $"A {traits.Alias} field {reason}.");

        if (!traits.HoldsValue)
        {
            const string NoValue = "holds no value";
            if (field.Alias is not null)
            {
                Unsupported("alias", $"{NoValue}, so it has no alias");
            }

            if (field.FormerAliases.Count > 0)
            {
                Unsupported("formerAliases", $"{NoValue}, so it has no former aliases");
            }

            if (field.Sensitive)
            {
                Unsupported("sensitive", $"{NoValue}, so it is not sensitive");
            }

            if (!field.IncludeInEmails)
            {
                Unsupported("includeInEmails", $"{NoValue}, so mail has nothing to leave out");
            }

            if (field.Keep != FieldKeep.Always)
            {
                Unsupported("keep", $"{NoValue}, so it has nothing to keep");
            }
        }

        if (field.Label is not null && !traits.Supports(FieldFeatures.Label))
        {
            Unsupported("label", "has no label");
        }

        if (field.HideLabel && !traits.Supports(FieldFeatures.HideLabel))
        {
            Unsupported("hideLabel", "has no label to hide");
        }

        if (field.Help is not null && !traits.Supports(FieldFeatures.Help))
        {
            Unsupported("help", "has no help text");
        }

        if (field.Placeholder is not null && !traits.Supports(FieldFeatures.Placeholder))
        {
            Unsupported("placeholder", "has no placeholder");
        }

        if (field.Required && !traits.Supports(FieldFeatures.Required))
        {
            Unsupported("required", "cannot be required");
        }

        if (field.DefaultValue is { } defaultValue)
        {
            if (!traits.Supports(FieldFeatures.DefaultValue))
            {
                Unsupported("defaultValue", "has no default value");
            }
            else if (defaultValue.IsList && traits.KindOf(field) != FieldKind.Choices)
            {
                Unsupported("defaultValue", "takes one answer, so its default value is not a list");
            }
        }

        if (field.Prefill is not null && !traits.Supports(FieldFeatures.Prefill))
        {
            Unsupported("prefill", "is not prefilled");
        }

        if (field.Autocomplete is not null && !traits.Supports(FieldFeatures.Autocomplete))
        {
            Unsupported("autocomplete", "has no autocomplete token");
        }

        var messagesPath = P(path, "messages");
        foreach (var message in FieldMessageProperties.All)
        {
            if (message.Read(field.Messages) is not null && (traits.Messages & message.Kind) == 0)
            {
                Fail(DefinitionErrors.PropertyNotSupported, P(messagesPath, message.Name), $"A {traits.Alias} field raises no '{message.Name}' error.");
            }
        }

        if (field.Options is not null && !traits.Supports(FieldFeatures.Options))
        {
            Unsupported("options", "has no options");
        }

        if (field.Rows.Count > 0 && !traits.Supports(FieldFeatures.Rows))
        {
            Unsupported("rows", "has no rows");
        }

        if (field.Fields.Count > 0 && !traits.Supports(FieldFeatures.ChildFields))
        {
            Unsupported("fields", "has no child fields");
        }
    }

    // The alias and the former aliases of a field: well formed, not reserved, and used once in the form in any letter
    // case, because the database compares aliases without case (ADR-0033) and merge tags outside the form resolve
    // former aliases.
    private void Aliases(FormField field, string path)
    {
        if (field.Alias is { } alias)
        {
            Alias(alias, P(path, "alias"));
        }

        var formerPath = P(path, "formerAliases");
        for (var i = 0; i < field.FormerAliases.Count; i++)
        {
            Alias(field.FormerAliases[i], I(formerPath, i));
        }
    }

    private void Alias(string alias, string path)
    {
        if (!IsAlias(alias))
        {
            Fail(DefinitionErrors.AliasInvalid, path, "An alias is a lowercase letter, then letters and digits, 64 characters at most.");
        }
        else if (ReservedAliases.Contains(alias))
        {
            Fail(DefinitionErrors.AliasReserved, path, $"'{alias}' is a reserved name (section 4.3.1).");
        }
        else if (!_aliases.TryAdd(alias, path))
        {
            Fail(DefinitionErrors.AliasDuplicate, path, $"The alias '{alias}' is already used at {_aliases[alias]} (letter case does not count).");
        }
    }

    private void CheckOptions(FormField field, FieldTypeTraits traits, string path)
    {
        var optionsPath = P(path, "options");
        if (field.Options is not { } options)
        {
            if (traits.Supports(FieldFeatures.NeedsOptions))
            {
                Fail(DefinitionErrors.PartMissing, optionsPath, $"A {traits.Alias} field has options.");
            }

            return;
        }

        if (!traits.Supports(FieldFeatures.Options))
        {
            return;
        }

        var itemsPath = P(optionsPath, "items");
        var listPath = P(optionsPath, "list");
        var configPath = P(optionsPath, "config");
        switch (options.Source)
        {
            case OptionSource.Items:
                if (options.OptionList is not null)
                {
                    Fail(DefinitionErrors.OptionsInvalid, listPath, "A field with its own options names no library list.");
                }

                if (options.Config is not null)
                {
                    Fail(DefinitionErrors.OptionsInvalid, configPath, "A field's own options need no source configuration.");
                }

                if (options.Items.Count == 0 && traits.Supports(FieldFeatures.NeedsOptions))
                {
                    Fail(DefinitionErrors.PartMissing, itemsPath, $"A {traits.Alias} field with its own options has at least one.");
                }

                break;
            case OptionSource.List:
                if (options.OptionList is null)
                {
                    Fail(DefinitionErrors.PartMissing, listPath, "Options from the library name the list.");
                }

                if (options.Items.Count > 0)
                {
                    Fail(DefinitionErrors.OptionsInvalid, itemsPath, "Options from a library list have no items of their own.");
                }

                if (options.Config is not null)
                {
                    Fail(DefinitionErrors.OptionsInvalid, configPath, "Options from a library list take their configuration from the list.");
                }

                break;
            default:
                if (options.Items.Count > 0)
                {
                    Fail(DefinitionErrors.OptionsInvalid, itemsPath, "Options from a source have no items of their own.");
                }

                if (options.OptionList is not null)
                {
                    Fail(DefinitionErrors.OptionsInvalid, listPath, "Options from a source other than the library name no list.");
                }

                break;
        }

        var parentValues = Parent(field, options, optionsPath);
        if (options.Source == OptionSource.Items)
        {
            Items(options.Items, itemsPath, traits, parentValues, hasParent: options.Parent is not null);
        }
    }

    // The values of the parent's own options when the parent field is a choice field with own options, an empty set
    // when it is one whose options come from elsewhere, and null when there is no valid parent.
    private HashSet<string>? Parent(FormField field, FieldOptions options, string optionsPath)
    {
        if (options.Parent is not { } parent)
        {
            return null;
        }

        var parentPath = P(optionsPath, "parent");
        if (parent == field.Key)
        {
            Fail(DefinitionErrors.InvalidReference, parentPath, "A field's options are not filtered by its own answer.");
            return null;
        }

        if (!_fields.TryGetValue(parent, out var entry) || !entry.Resolves)
        {
            Fail(DefinitionErrors.UnknownField, parentPath, "The parent names no field of the form.");
            return null;
        }

        if (entry.Unknown)
        {
            return null;
        }

        if (entry.Row is not null || !entry.Traits!.Supports(FieldFeatures.Options) || entry.Field.Options is null)
        {
            Fail(DefinitionErrors.InvalidReference, parentPath, "A parent is a choice field with options.");
            return null;
        }

        _parents.Add((field.Key, parent, parentPath));
        return entry.Field.Options.Source == OptionSource.Items
            ? entry.Field.Options.Items.Select(item => item.Value).ToHashSet(StringComparer.Ordinal)
            : [];
    }

    // Option values are what entries store and filters compare: cleaned as answers are, and unique in the field once
    // folded as rules compare text (ADR-0005), because the entries screen filters by folded text (ADR-0033).
    private void Items(IReadOnlyList<FieldOption> items, string path, FieldTypeTraits traits, HashSet<string>? parentValues, bool hasParent)
    {
        var folded = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var itemPath = I(path, i);
            var valuePath = P(itemPath, "value");
            var clean = TextSemantics.Clean(item.Value);
            if (clean.Length == 0)
            {
                Fail(DefinitionErrors.OptionValueInvalid, valuePath, "An option's value is more than spaces.");
            }
            else if (!string.Equals(clean, item.Value, StringComparison.Ordinal))
            {
                Fail(DefinitionErrors.OptionValueInvalid, valuePath, "An option's value has no spaces at its ends and is in composed form (NFC), as answers are when they are compared.");
            }
            else if (!folded.TryAdd(TextSemantics.Fold(clean), i))
            {
                Fail(DefinitionErrors.OptionValueDuplicate, valuePath, $"The value equals that of option {folded[TextSemantics.Fold(clean)] + 1} once letter case is ignored.");
            }

            if (item.Places is not null && !traits.Supports(FieldFeatures.Places))
            {
                Fail(DefinitionErrors.PropertyNotSupported, P(itemPath, "places"), $"A {traits.Alias} field's options have no places.");
            }

            var parentValuesPath = P(itemPath, "parentValues");
            if (item.ParentValues.Count > 0 && !hasParent)
            {
                Fail(DefinitionErrors.OptionsInvalid, parentValuesPath, "Parent values belong to options a parent field filters.");
            }
            else if (parentValues is { Count: > 0 })
            {
                for (var j = 0; j < item.ParentValues.Count; j++)
                {
                    if (!parentValues.Contains(item.ParentValues[j]))
                    {
                        Fail(DefinitionErrors.InvalidReference, I(parentValuesPath, j), $"The parent field has no option '{item.ParentValues[j]}'.");
                    }
                }
            }
        }
    }

    private void CheckValidation(FormField field, FieldTypeTraits traits, string path)
    {
        var validationPath = P(path, "validation");
        for (var i = 0; i < field.Validation.Count; i++)
        {
            var checkPath = I(validationPath, i);
            switch (field.Validation[i])
            {
                case PatternCheck pattern when traits.Supports(FieldFeatures.PatternCheck):
                    if (PatternProblem(pattern.Pattern) is { } problem)
                    {
                        Fail(DefinitionErrors.UnsafePattern, P(checkPath, "pattern"), problem);
                    }

                    break;
                case MatchesFieldCheck matches when traits.Supports(FieldFeatures.MatchesFieldCheck):
                    MatchesField(field, matches.Field, P(checkPath, "field"));
                    break;
                case UniquePerFormCheck when traits.Supports(FieldFeatures.UniquePerFormCheck):
                    break;
                default:
                    Fail(DefinitionErrors.PropertyNotSupported, checkPath, $"A {traits.Alias} field takes no check of this kind.");
                    break;
            }
        }
    }

    private void MatchesField(FormField field, Guid other, string path)
    {
        if (other == field.Key)
        {
            Fail(DefinitionErrors.InvalidReference, path, "A field's answer always matches itself; name another field.");
        }
        else if (!_fields.TryGetValue(other, out var entry) || !entry.Resolves)
        {
            Fail(DefinitionErrors.UnknownField, path, "The check names no field of the form that holds a value.");
        }
        else if (!entry.Unknown && !_kinds.ContainsKey(KeyText(other)))
        {
            Fail(DefinitionErrors.InvalidReference, path, $"The answers of {entry.Describe} cannot be compared.");
        }
        else if (!entry.Unknown && !_visitorKinds.ContainsKey(KeyText(other)))
        {
            Fail(DefinitionErrors.InvalidReference, path, ServerValue(entry));
        }
    }

    // Rules and checks on the page run in the visitor's browser as well, which never gets such a value (ADR-0035).
    private static string ServerValue(Entry entry) =>
        $"The value of {entry.Describe} is set on the server when the entry is submitted, so the visitor's browser cannot compare it; a condition on the query value or the member can look at its source.";

    private static string? PatternProblem(string pattern)
    {
        try
        {
            SafePattern.Compile(pattern);
            return null;
        }
        catch (LogicException)
        {
            return "The pattern uses syntax outside the safe subset or exceeds its limits (ADR-0005).";
        }
    }

    /// <summary>A field, a repeatable group child or a grid row, as references find it.</summary>
    /// <param name="Field">The field, or the grid a row belongs to.</param>
    /// <param name="Traits">The field's type; null when the site has no such type.</param>
    /// <param name="Node">The field's node: a row is shown with its grid.</param>
    /// <param name="InRepeater">Whether the field is a child of a repeatable group.</param>
    /// <param name="Row">The grid row, for a row.</param>
    private sealed record Entry(FormField Field, FieldTypeTraits? Traits, int Node, bool InRepeater, GridRow? Row)
    {
        /// <summary>Whether the field's type, or for a row its grid's, is unknown: references to it wait until it has one.</summary>
        public bool Unknown => Traits is null;

        /// <summary>Whether a reference to a value may name it: a row, a field that holds a value, or one of unknown type.</summary>
        public bool Resolves => Row is not null || Traits is null || Traits.HoldsValue;

        /// <summary>The field or row, for messages.</summary>
        public string Describe => Row is not null ? $"a row of {Name(Field)}" : $"{Name(Field)} ({Field.Type})";
    }

    /// <summary>A page, group or field in the graph of what decides whether it is shown.</summary>
    /// <param name="Path">The element's path.</param>
    /// <param name="Description">The element, for messages.</param>
    private sealed record Node(string Path, string Description)
    {
        /// <summary>The nodes whose being shown this one depends on: its container, and what its show or hide rule looks at.</summary>
        public List<int> Edges { get; } = [];

        /// <summary>The path of the element's first switched-on show or hide rule, where a cycle through it is reported.</summary>
        public string? RulePath { get; set; }
    }
}
