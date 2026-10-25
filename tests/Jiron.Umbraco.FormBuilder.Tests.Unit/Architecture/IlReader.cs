using System.Reflection;
using System.Reflection.Emit;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Architecture;

/// <summary>
/// Reads the instructions of a method's IL for the architecture tests that look at what Core's compiled code does
/// (ADR-0036's connection ban, ADR-0033's stored names): the bodies of async methods and lambdas are methods of the
/// types the compiler writes, so reading every type's methods reads them too.
/// </summary>
internal static class IlReader
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly OpCode[] OneByte = new OpCode[256];
    private static readonly OpCode[] TwoByte = new OpCode[256];

#pragma warning disable CA1810 // The tables are filled from the list of operations, once.
    static IlReader()
#pragma warning restore CA1810
    {
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var code = (OpCode)field.GetValue(null)!;
            var value = (ushort)code.Value;
            if (code.Size == 1)
            {
                OneByte[value] = code;
            }
            else
            {
                TwoByte[value & 0xFF] = code;
            }
        }
    }

    /// <summary>The methods and constructors a type declares itself.</summary>
    public static IEnumerable<MethodBase> Methods(Type type) => type.GetMethods(Declared).Cast<MethodBase>().Concat(type.GetConstructors(Declared));

    /// <summary>The generic arguments the tokens of <paramref name="method"/> resolve against.</summary>
    public static (Type[]? Type, Type[]? Method) Context(MethodBase method) =>
        (method.DeclaringType is { IsGenericType: true } type ? type.GetGenericArguments() : null,
         method is MethodInfo { IsGenericMethod: true } generic ? generic.GetGenericArguments() : null);

    /// <summary>The instructions of <paramref name="method"/>; none for a method without a body.</summary>
    /// <exception cref="InvalidDataException">The IL holds an operation this reader does not know.</exception>
    public static IEnumerable<IlInstruction> Read(MethodBase method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var il = method.GetMethodBody()?.GetILAsByteArray();
        if (il is null)
        {
            yield break;
        }

        var at = 0;
        while (at < il.Length)
        {
            var start = at;
            var code = il[at] == 0xFE ? TwoByte[il[++at]] : OneByte[il[at]];
            at++;
            if (code.Size == 0)
            {
                throw new InvalidDataException($"an unknown operation at {start}");
            }

            var token = code.OperandType is OperandType.InlineMethod or OperandType.InlineType or OperandType.InlineTok or OperandType.InlineField
                ? BitConverter.ToInt32(il, at)
                : 0;
            at += OperandSize(code, il, at);
            yield return new IlInstruction(code, start, token);
        }
    }

    private static int OperandSize(OpCode code, byte[] il, int at) => code.OperandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, at)),
        _ => 4,
    };
}

/// <summary>One instruction of a method's IL.</summary>
/// <param name="Code">The operation.</param>
/// <param name="Offset">Where it starts in the method's IL.</param>
/// <param name="Token">The token of the member or type it names, or 0 when it names none.</param>
internal readonly record struct IlInstruction(OpCode Code, int Offset, int Token);
