namespace Jiron.Umbraco.FormBuilder.Logic;

/// <summary>Calculations over exact decimals, and numbers as they are stored (ADR-0005); step 5.3 adds the expression language.</summary>
internal static class Calculations
{
    /// <summary>Evaluates a calculation: each operation's shape is checked, then its arguments run left to right.</summary>
    public static ExactDecimal Calculate(Calculation calculation)
    {
        if (calculation is Constant constant)
        {
            return ExactDecimal.Operand(constant.Text);
        }

        var (op, args, scale) = (Operation)calculation;
        Func<ExactDecimal, ExactDecimal, ExactDecimal>? binary = op switch
        {
            "add" => ExactDecimal.Add,
            "sub" => ExactDecimal.Subtract,
            "mul" => ExactDecimal.Multiply,
            "div" => ExactDecimal.Divide,
            _ => null,
        };
        if (binary is not null)
        {
            return args.Count == 2
                ? binary(Calculate(args[0]), Calculate(args[1]))
                : throw new LogicException(LogicErrors.InvalidOperand);
        }

        if (op is not ("round" or "store"))
        {
            throw new LogicException(LogicErrors.UnknownOperator);
        }

        if (args.Count != 1)
        {
            throw new LogicException(LogicErrors.InvalidOperand);
        }

        if (op == "store")
        {
            return ExactDecimal.Stored(Calculate(args[0]));
        }

        return scale is >= 0 and <= ExactDecimal.MaxRoundPlaces
            ? ExactDecimal.Round(Calculate(args[0]), scale.Value)
            : throw new LogicException(LogicErrors.InvalidOperand);
    }

    /// <summary>A number as a field stores it: the wire form within the storage limits, in canonical form.</summary>
    public static string StoredNumber(string text) =>
        ExactDecimal.Stored(ExactDecimal.Parse(text) ?? throw new LogicException(LogicErrors.NotANumber)).ToString();
}
