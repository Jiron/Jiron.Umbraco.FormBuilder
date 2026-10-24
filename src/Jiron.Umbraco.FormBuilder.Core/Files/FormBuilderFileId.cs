using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace Jiron.Umbraco.FormBuilder.Core.Files;

/// <summary>
/// The ids of stored files (ADR-0006): 128 random bits as 32 lowercase hex characters, never derived from a file's name.
/// <c>jfbEntryFile.StoredName</c> holds them, and every <see cref="IFormBuilderFileStore"/> refuses anything else.
/// </summary>
public static class FormBuilderFileId
{
    /// <summary>The length of an id.</summary>
    public const int Length = 32;

    /// <summary>A new id from 128 random bits.</summary>
    public static string New() => RandomNumberGenerator.GetHexString(Length, lowercase: true);

    /// <summary>Whether <paramref name="id"/> is 32 lowercase hex characters.</summary>
    public static bool IsValid([NotNullWhen(true)] string? id) =>
        id is { Length: Length } && id.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f');

    /// <summary>Throws unless <paramref name="id"/> is 32 lowercase hex characters, for stores to check their ids with.</summary>
    /// <exception cref="ArgumentException">The id is not a file id.</exception>
    public static void ThrowIfInvalid([NotNull] string? id, [CallerArgumentExpression(nameof(id))] string? parameterName = null)
    {
        if (!IsValid(id))
        {
            throw new ArgumentException($"A file id is {Length} lowercase hex characters.", parameterName);
        }
    }
}
