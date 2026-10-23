namespace Jiron.Umbraco.FormBuilder.Logic;

/// <summary>
/// A problem the shared logic reports instead of a result (ADR-0005). The code is the contract: both runtimes use the
/// same codes (<see cref="LogicErrors"/>), and the vector files expect them as <c>{ "error": code }</c>.
/// </summary>
internal sealed class LogicException : Exception
{
    public LogicException()
        : this(LogicErrors.InvalidOperand)
    {
    }

    public LogicException(string code)
        : base(code) => Code = code;

    public LogicException(string code, Exception innerException)
        : base(code, innerException) => Code = code;

    /// <summary>One of <see cref="LogicErrors"/>.</summary>
    public string Code { get; }
}

/// <summary>The codes the shared logic reports, the same in C# and TypeScript (ADR-0005).</summary>
internal static class LogicErrors
{
    /// <summary>A condition or operand names a field the form does not have, or a context it does not know.</summary>
    public const string UnknownField = "unknown_field";

    /// <summary>A condition or calculation uses an operator that does not exist.</summary>
    public const string UnknownOperator = "unknown_operator";

    /// <summary>The operator exists but does not take the kind of the field (or context) it looks at.</summary>
    public const string OperatorNotForKind = "operator_not_for_kind";

    /// <summary>An operand is missing, superfluous or of the wrong shape.</summary>
    public const string InvalidOperand = "invalid_operand";

    /// <summary>A condition names both a field and a context or neither, or a group matches neither "all" nor "any".</summary>
    public const string InvalidCondition = "invalid_condition";

    /// <summary>A pattern uses syntax outside the safe subset or exceeds its limits.</summary>
    public const string UnsafePattern = "unsafe_pattern";

    /// <summary>The instant of evaluation is not a valid instant in the wire form.</summary>
    public const string InvalidContext = "invalid_context";

    /// <summary>The site's time zone is not an IANA time zone this runtime knows.</summary>
    public const string UnknownTimeZone = "unknown_time_zone";

    /// <summary>A number is not in the wire form.</summary>
    public const string NotANumber = "not_a_number";

    /// <summary>A number has more digits than a stored number may have.</summary>
    public const string TooManyDigits = "too_many_digits";

    /// <summary>A calculation's intermediate result has more than 60 digits.</summary>
    public const string OutOfRange = "out_of_range";

    /// <summary>A calculation divides by zero.</summary>
    public const string DivisionByZero = "division_by_zero";
}
