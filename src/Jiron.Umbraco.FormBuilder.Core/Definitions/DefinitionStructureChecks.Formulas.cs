using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Core.Definitions;

/// <summary>The formulas of calculations (ADR-0063).</summary>
internal sealed partial class DefinitionStructureChecks
{
    /// <summary>
    /// A calculation's formula: read, then checked against the fields the visitor's browser has, as the form engine
    /// checks it there. The fields it names join the graph of what decides whether an element is shown, because a
    /// calculation is worked out after them, so one that depends on itself is a cycle there.
    /// </summary>
    private void CheckFormula(string formula, string path, int node)
    {
        Calculation tree;
        try
        {
            tree = Calculations.Parse(formula);
        }
        catch (LogicException exception)
        {
            Fail(exception.Code, path, FormulaMistake(exception, alias: null));
            return;
        }

        var references = new List<FieldReference>();
        References(tree, references);
        var waits = false;
        foreach (var reference in references)
        {
            if (Named(reference.Alias) is { } entry)
            {
                _nodes[node].Edges.Add(entry.Node);
                _nodes[node].FormulaPath ??= path;
                waits |= entry.Unknown;
            }
        }

        // A formula that names a field whose type the site lacks waits for the type, as a condition does.
        if (waits)
        {
            return;
        }

        try
        {
            Calculations.Check(tree, alias => Named(alias) is { } entry && _visitorKinds.TryGetValue(KeyText(entry.Field.Key), out var kind) ? kind : null);
        }
        catch (LogicException exception)
        {
            var alias = references.Find(reference => reference.At == exception.At)?.Alias;
            if (exception.Code == LogicErrors.UnknownField && alias is not null && Named(alias) is { } entry && _kinds.ContainsKey(KeyText(entry.Field.Key)))
            {
                Fail(DefinitionErrors.InvalidReference, path, $"The value of {entry.Describe} is set on the server when the entry is submitted, so the visitor's browser cannot work out a formula over it{Where(exception)}.");
                return;
            }

            Fail(exception.Code, path, FormulaMistake(exception, alias));
        }
    }

    // The field an alias names, as the form engine finds it: the first field with the alias, compared exactly.
    private Entry? Named(string alias) =>
        _aliasKeys.TryGetValue(alias, out var key) && _fields.TryGetValue(key, out var entry) ? entry : null;

    // The fields a formula names, in the order they are written.
    private static void References(Calculation item, List<FieldReference> found)
    {
        if (item is FieldReference reference)
        {
            found.Add(reference);
        }
        else if (item is Operation operation)
        {
            foreach (var arg in operation.Args)
            {
                References(arg, found);
            }
        }
    }

    private static string Where(LogicException exception) => exception.At is { } at ? $" at character {at + 1}" : string.Empty;

    private string FormulaMistake(LogicException exception, string? alias)
    {
        var where = Where(exception);
        return exception.Code switch
        {
            LogicErrors.InvalidFormula => $"The formula does not follow the grammar of calculations{where}.",
            LogicErrors.UnknownOperator => $"There is no such function{where}; a formula calls min, max, round, if, and, or, not, daysBetween, yearsBetween and today.",
            LogicErrors.InvalidOperand => $"The arguments do not fit the function{where}: their number, round's places (a whole number 0 to 20 as it is written), or a date, which is a date field or today() inside daysBetween and yearsBetween.",
            LogicErrors.TooDeep => $"Parentheses and calls nest more than {Calculations.MaxDepth} levels deep{where}.",
            LogicErrors.OutOfRange => $"The number has more than {ExactDecimal.MaxDigits} digits{where}.",
            LogicErrors.UnknownField when alias is not null && Named(alias) is { } entry => $"A formula cannot read the answers of {entry.Describe}{where}.",
            LogicErrors.UnknownField => $"The form has no field '{alias}'{where}.",
            LogicErrors.OperatorNotForKind => $"The field '{alias}' does not fit where it stands{where}: arithmetic takes numbers, choices and checkboxes, and daysBetween and yearsBetween take date fields.",
            LogicErrors.ConstantFormula => "The formula names no field, so its result never changes.",
            _ => $"The formula has a mistake{where}.",
        };
    }
}
