using System.Globalization;

namespace MicroKit.Domain;

/// <summary>
/// The single definition of a code point that a value object reading text from outside the domain
/// refuses because it is invisible or breaks a line: <see cref="ValueObjects.Common.Actor"/> and
/// <see cref="ValueObjects.Common.Email"/> apply it (ADR-004 D8, D5).
/// </summary>
/// <remarks>
/// <para>
/// Refused: a control character (Cc), a format character (Cf), a line or paragraph separator (Zl,
/// Zp), an unassigned code point (Cn), an unpaired surrogate, and every Unicode
/// <c>Default_Ignorable_Code_Point</c>, the characters a renderer draws as nothing: the Hangul
/// fillers, the variation selectors, U+034F and the like. Two values differing only by such a
/// character would look identical, and a line break would let a value forge a line in a log.
/// </para>
/// <para>
/// The categories come from the Unicode data of the running .NET version. An unassigned code point is
/// refused, so a later version assigning it can only make the rule accept more, never reject a value
/// accepted today.
/// </para>
/// </remarks>
internal static class UnicodeText
{
    /// <summary>
    /// Returns the code point at <paramref name="index"/>, decoding a surrogate pair, or
    /// <see langword="null"/> for an unpaired surrogate.
    /// </summary>
    /// <param name="text">The text to read.</param>
    /// <param name="index">The index of the first UTF-16 unit of the code point.</param>
    /// <param name="length">The number of UTF-16 units the code point occupies, 1 or 2.</param>
    internal static int? CodePointAt(string text, int index, out int length)
    {
        var c = text[index];
        length = 1;

        if (char.IsHighSurrogate(c) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
        {
            length = 2;
            return char.ConvertToUtf32(c, text[index + 1]);
        }

        return char.IsSurrogate(c) ? null : c;
    }

    /// <summary>
    /// Whether <paramref name="text"/> holds only code points that are visible or a plain space.
    /// </summary>
    internal static bool IsVisible(string text)
    {
        var i = 0;
        while (i < text.Length)
        {
            if (CodePointAt(text, i, out var length) is not { } codePoint || IsRefused(codePoint))
                return false;

            i += length;
        }

        return true;
    }

    /// <summary>
    /// Whether a code point is refused: invisible, line-breaking, control or unassigned.
    /// </summary>
    internal static bool IsRefused(int codePoint) =>
        CharUnicodeInfo.GetUnicodeCategory(codePoint) is UnicodeCategory.Control or UnicodeCategory.Format
            or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator or UnicodeCategory.OtherNotAssigned
        || IsDefaultIgnorable(codePoint);

    // Default_Ignorable_Code_Point, Unicode DerivedCoreProperties.txt. Most of it is Cf or Cn and
    // already refused by category; the ranges listed are the ones that are letters or marks.
    private static bool IsDefaultIgnorable(int codePoint) => codePoint is
        0x00AD or 0x034F or 0x061C or 0x115F or 0x1160 or 0x17B4 or 0x17B5
        or (>= 0x180B and <= 0x180F) or (>= 0x200B and <= 0x200F) or (>= 0x202A and <= 0x202E)
        or (>= 0x2060 and <= 0x206F) or 0x3164 or (>= 0xFE00 and <= 0xFE0F) or 0xFEFF or 0xFFA0
        or (>= 0xFFF0 and <= 0xFFF8) or (>= 0x1BCA0 and <= 0x1BCA3) or (>= 0x1D173 and <= 0x1D17A)
        or (>= 0xE0000 and <= 0xE0FFF);
}
