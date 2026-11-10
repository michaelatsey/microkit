using System.Buffers;
using System.Buffers.Binary;
using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

namespace MicroKit.Persistence;

/// <summary>
/// The position after which a keyset page starts: an opaque token, issued as <see cref="KeysetPage{T}.Next"/>
/// by the page before.
/// </summary>
/// <remarks>
/// <para>
/// Send <see cref="ToString"/> to a client and read it back with <see cref="TryParse"/>. The token is untrusted
/// input. <see cref="TryParse"/> checks its format only and never allocates more than the token's own length
/// allows, so an application can answer <c>400</c> at its boundary. The read repository checks the rest when it
/// runs the query, and throws <see cref="ArgumentException"/>:
/// <list type="bullet">
///   <item><description>the token was issued for another aggregate type or another ordering (its fingerprint covers
///     the root's model name, the sort keys and their directions);</description></item>
///   <item><description>its values do not match the sort keys' types.</description></item>
/// </list>
/// </para>
/// <para>
/// The token carries, in Base64Url, the last row's sort-key values as the database stores them: through EF Core,
/// the provider values, after the key properties' value converters. The in-memory doubles have no converters, so
/// their token carries a handle into the double that issued it. It is not encrypted: do not sort by a secret.
/// </para>
/// </remarks>
public sealed class KeysetCursor
{
    /// <summary>The longest token <see cref="TryParse"/> accepts, in characters.</summary>
    public const int MaxTokenLength = 1024;

    private const byte FormatVersion = 1;
    private const int FingerprintLength = 8;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly byte[] _fingerprint;
    private readonly string _token;

    private KeysetCursor(byte[] fingerprint, object[] values, string token)
    {
        _fingerprint = fingerprint;
        Values = values;
        _token = token;
    }

    /// <summary>Gets the sort-key values, as provider values, in key order.</summary>
    internal IReadOnlyList<object> Values { get; }

    /// <summary>Returns the token: Base64Url, at most <see cref="MaxTokenLength"/> characters.</summary>
    /// <returns>The token to hand to a client.</returns>
    public override string ToString() => _token;

    /// <summary>
    /// Reads a token issued by <see cref="ToString"/>. Checks its format only: its length, its encoding, and every
    /// declared length against the bytes that remain.
    /// </summary>
    /// <param name="token">The token, as received.</param>
    /// <param name="cursor">The cursor, when the token is well formed.</param>
    /// <returns><see langword="true"/> when <paramref name="token"/> is a well-formed cursor.</returns>
    public static bool TryParse([NotNullWhen(true)] string? token, [NotNullWhen(true)] out KeysetCursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrEmpty(token) || token.Length > MaxTokenLength)
            return false;

        // Checked before decoding (TryDecodeFromChars throws on an invalid character); the buffer is bounded by
        // MaxTokenLength: the only allocation before the token is known to be well formed.
        if (!Base64Url.IsValid(token, out var length))
            return false;
        var bytes = new byte[length];
        if (!Base64Url.TryDecodeFromChars(token, bytes, out var written))
            return false;

        if (!TryDecode(bytes.AsSpan(0, written), out var fingerprint, out var values))
            return false;

        // One spelling per cursor: a token that decodes but does not re-encode to itself is refused.
        if (!string.Equals(Base64Url.EncodeToString(bytes.AsSpan(0, written)), token, StringComparison.Ordinal))
            return false;

        cursor = new KeysetCursor(fingerprint, values, token);
        return true;
    }

    /// <summary>Gets a value indicating whether a sort key of provider type <paramref name="type"/> can go in a cursor.</summary>
    internal static bool IsSupported(Type type) => TagOf(type) != Tag.None;

    /// <summary>
    /// The most bytes one value of <paramref name="type"/> takes in a cursor, its tag included, or
    /// <see langword="null"/> when nothing bounds it: a <see cref="string"/> or <see cref="byte"/> array without
    /// <paramref name="maxLength"/>. A string counts 4 UTF-8 bytes per character, its worst case whether the column
    /// counts UTF-16 units or code points.
    /// </summary>
    internal static long? MaxEncodedLength(Type type, int? maxLength) => TagOf(type) switch
    {
        Tag.Boolean or Tag.Byte or Tag.SByte => 1 + 1,
        Tag.Int16 or Tag.UInt16 => 1 + 2,
        Tag.Int32 or Tag.UInt32 or Tag.Single or Tag.DateOnly => 1 + 4,
        Tag.Int64 or Tag.UInt64 or Tag.Double or Tag.DateTime or Tag.TimeOnly => 1 + 8,
        Tag.DateTimeOffset => 1 + 8 + 2,
        Tag.Decimal or Tag.Guid => 1 + 16,
        Tag.String => maxLength is { } characters and >= 0 ? 1 + 4 + (4L * characters) : null,
        Tag.Bytes => maxLength is { } length and >= 0 ? 1 + 4 + (long)length : null,
        _ => null,
    };

    /// <summary>The length of the token whose values take <paramref name="valueBytes"/> bytes: Base64Url, unpadded.</summary>
    internal static long TokenLengthFor(long valueBytes)
    {
        var bytes = 1 + FingerprintLength + 1 + valueBytes; // version, fingerprint, count
        return ((bytes * 4) + 2) / 3;
    }

    /// <summary>
    /// Issues the cursor of <paramref name="values"/>, for the ordering of <paramref name="root"/> by
    /// <paramref name="keys"/>.
    /// </summary>
    /// <exception cref="ArgumentException">A value's type is not one a cursor carries.</exception>
    /// <exception cref="InvalidOperationException">The values do not fit in <see cref="MaxTokenLength"/>.</exception>
    internal static KeysetCursor Create(string root, IReadOnlyList<(string Name, bool Descending)> keys, IReadOnlyList<object> values) =>
        Create(FingerprintOf(root, keys), values);

    /// <summary>Issues the cursor of <paramref name="values"/> under a fingerprint computed once by <see cref="FingerprintOf"/>.</summary>
    /// <exception cref="ArgumentException">A value's type is not one a cursor carries.</exception>
    /// <exception cref="InvalidOperationException">The values do not fit in <see cref="MaxTokenLength"/>.</exception>
    internal static KeysetCursor Create(byte[] fingerprint, IReadOnlyList<object> values)
    {
        var writer = new ArrayBufferWriter<byte>();
        writer.Write([FormatVersion]);
        writer.Write(fingerprint);
        if (values.Count > byte.MaxValue)
            throw new ArgumentException($"A cursor carries at most {byte.MaxValue} values.", nameof(values));
        writer.Write([(byte)values.Count]);
        foreach (var value in values)
            WriteValue(writer, value);

        var token = Base64Url.EncodeToString(writer.WrittenSpan);
        if (token.Length > MaxTokenLength)
        {
            throw new InvalidOperationException(
                $"The sort-key values of this row need a {token.Length}-character cursor, above {nameof(KeysetCursor)}.{nameof(MaxTokenLength)} " +
                $"({MaxTokenLength}). Sort by shorter keys.");
        }

        return new KeysetCursor([.. fingerprint], [.. values], token);
    }

    /// <summary>Gets a value indicating whether this cursor was issued for the ordering of <paramref name="root"/> by <paramref name="keys"/>.</summary>
    internal bool IsFor(string root, IReadOnlyList<(string Name, bool Descending)> keys) => IsFor(FingerprintOf(root, keys));

    /// <summary>Gets a value indicating whether this cursor carries <paramref name="fingerprint"/>.</summary>
    internal bool IsFor(byte[] fingerprint) => _fingerprint.AsSpan().SequenceEqual(fingerprint);

    /// <summary>
    /// The fingerprint of an ordering: the first bytes of a SHA-256 over the root's name and each key's name and
    /// direction, each name length-prefixed so no two orderings share an input.
    /// </summary>
    internal static byte[] FingerprintOf(string root, IReadOnlyList<(string Name, bool Descending)> keys)
    {
        var input = new ArrayBufferWriter<byte>();
        WriteName(input, root);
        foreach (var (name, descending) in keys)
        {
            WriteName(input, name);
            input.Write([descending ? (byte)'D' : (byte)'A']);
        }

        return SHA256.HashData(input.WrittenSpan)[..FingerprintLength];
    }

    private static void WriteName(ArrayBufferWriter<byte> writer, string name)
    {
        var bytes = Encoding.UTF8.GetBytes(name);
        WriteInt32(writer, bytes.Length);
        writer.Write(bytes);
    }

    private enum Tag : byte
    {
        None = 0,
        Boolean = 1,
        Byte = 2,
        SByte = 3,
        Int16 = 4,
        UInt16 = 5,
        Int32 = 6,
        UInt32 = 7,
        Int64 = 8,
        UInt64 = 9,
        Single = 10,
        Double = 11,
        Decimal = 12,
        Guid = 13,
        String = 14,
        DateTime = 15,
        DateTimeOffset = 16,
        DateOnly = 17,
        TimeOnly = 18,
        Bytes = 19,
    }

    private static Tag TagOf(Type type) =>
        type == typeof(bool) ? Tag.Boolean
        : type == typeof(byte) ? Tag.Byte
        : type == typeof(sbyte) ? Tag.SByte
        : type == typeof(short) ? Tag.Int16
        : type == typeof(ushort) ? Tag.UInt16
        : type == typeof(int) ? Tag.Int32
        : type == typeof(uint) ? Tag.UInt32
        : type == typeof(long) ? Tag.Int64
        : type == typeof(ulong) ? Tag.UInt64
        : type == typeof(float) ? Tag.Single
        : type == typeof(double) ? Tag.Double
        : type == typeof(decimal) ? Tag.Decimal
        : type == typeof(Guid) ? Tag.Guid
        : type == typeof(string) ? Tag.String
        : type == typeof(DateTime) ? Tag.DateTime
        : type == typeof(DateTimeOffset) ? Tag.DateTimeOffset
        : type == typeof(DateOnly) ? Tag.DateOnly
        : type == typeof(TimeOnly) ? Tag.TimeOnly
        : type == typeof(byte[]) ? Tag.Bytes
        : Tag.None;

    private static void WriteValue(ArrayBufferWriter<byte> writer, object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var tag = TagOf(value.GetType());
        if (tag == Tag.None)
            throw new ArgumentException($"A cursor cannot carry a value of type '{value.GetType().Name}'.", nameof(value));

        writer.Write([(byte)tag]);
        switch (value)
        {
            case bool v: writer.Write([v ? (byte)1 : (byte)0]); break;
            case byte v: writer.Write([v]); break;
            case sbyte v: writer.Write([unchecked((byte)v)]); break;
            case short v: BinaryPrimitives.WriteInt16LittleEndian(writer.GetSpan(2), v); writer.Advance(2); break;
            case ushort v: BinaryPrimitives.WriteUInt16LittleEndian(writer.GetSpan(2), v); writer.Advance(2); break;
            case int v: WriteInt32(writer, v); break;
            case uint v: BinaryPrimitives.WriteUInt32LittleEndian(writer.GetSpan(4), v); writer.Advance(4); break;
            case long v: WriteInt64(writer, v); break;
            case ulong v: BinaryPrimitives.WriteUInt64LittleEndian(writer.GetSpan(8), v); writer.Advance(8); break;
            case float v: BinaryPrimitives.WriteSingleLittleEndian(writer.GetSpan(4), v); writer.Advance(4); break;
            case double v: BinaryPrimitives.WriteDoubleLittleEndian(writer.GetSpan(8), v); writer.Advance(8); break;
            case decimal v:
                Span<int> bits = stackalloc int[4];
                decimal.GetBits(v, bits);
                foreach (var part in bits)
                    WriteInt32(writer, part);
                break;
            case Guid v: v.TryWriteBytes(writer.GetSpan(16)); writer.Advance(16); break;
            case string v:
                var text = StrictUtf8.GetBytes(v);
                WriteInt32(writer, text.Length);
                writer.Write(text);
                break;
            case DateTime v: WriteInt64(writer, v.ToBinary()); break;
            case DateTimeOffset v:
                WriteInt64(writer, v.Ticks);
                BinaryPrimitives.WriteInt16LittleEndian(writer.GetSpan(2), checked((short)v.Offset.TotalMinutes));
                writer.Advance(2);
                break;
            case DateOnly v: WriteInt32(writer, v.DayNumber); break;
            case TimeOnly v: WriteInt64(writer, v.Ticks); break;
            case byte[] v:
                WriteInt32(writer, v.Length);
                writer.Write(v);
                break;
        }
    }

    private static void WriteInt32(ArrayBufferWriter<byte> writer, int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(writer.GetSpan(4), value);
        writer.Advance(4);
    }

    private static void WriteInt64(ArrayBufferWriter<byte> writer, long value)
    {
        BinaryPrimitives.WriteInt64LittleEndian(writer.GetSpan(8), value);
        writer.Advance(8);
    }

    // Every length the token declares is checked against what remains before anything is allocated.
    private static bool TryDecode(ReadOnlySpan<byte> bytes, out byte[] fingerprint, out object[] values)
    {
        fingerprint = [];
        values = [];
        if (bytes.Length < 1 + FingerprintLength + 1 || bytes[0] != FormatVersion)
            return false;

        var position = 1;
        var declaredFingerprint = bytes.Slice(position, FingerprintLength);
        position += FingerprintLength;
        var count = bytes[position++];
        if (count == 0 || count > bytes.Length - position) // every value takes at least its tag
            return false;

        var read = new object[count];
        for (var i = 0; i < count; i++)
        {
            if (!TryReadValue(bytes, ref position, out var value))
                return false;
            read[i] = value;
        }

        if (position != bytes.Length)
            return false;

        fingerprint = declaredFingerprint.ToArray();
        values = read;
        return true;
    }

    private static bool TryReadValue(ReadOnlySpan<byte> bytes, ref int position, [NotNullWhen(true)] out object? value)
    {
        value = null;
        if (position >= bytes.Length)
            return false;

        var tag = (Tag)bytes[position++];
        var rest = bytes[position..];
        try
        {
            switch (tag)
            {
                case Tag.Boolean when rest.Length >= 1 && rest[0] <= 1: value = rest[0] == 1; position += 1; break;
                case Tag.Byte when rest.Length >= 1: value = rest[0]; position += 1; break;
                case Tag.SByte when rest.Length >= 1: value = unchecked((sbyte)rest[0]); position += 1; break;
                case Tag.Int16 when rest.Length >= 2: value = BinaryPrimitives.ReadInt16LittleEndian(rest); position += 2; break;
                case Tag.UInt16 when rest.Length >= 2: value = BinaryPrimitives.ReadUInt16LittleEndian(rest); position += 2; break;
                case Tag.Int32 when rest.Length >= 4: value = BinaryPrimitives.ReadInt32LittleEndian(rest); position += 4; break;
                case Tag.UInt32 when rest.Length >= 4: value = BinaryPrimitives.ReadUInt32LittleEndian(rest); position += 4; break;
                case Tag.Int64 when rest.Length >= 8: value = BinaryPrimitives.ReadInt64LittleEndian(rest); position += 8; break;
                case Tag.UInt64 when rest.Length >= 8: value = BinaryPrimitives.ReadUInt64LittleEndian(rest); position += 8; break;
                case Tag.Single when rest.Length >= 4: value = BinaryPrimitives.ReadSingleLittleEndian(rest); position += 4; break;
                case Tag.Double when rest.Length >= 8: value = BinaryPrimitives.ReadDoubleLittleEndian(rest); position += 8; break;
                case Tag.Decimal when rest.Length >= 16:
                    value = new decimal([
                        BinaryPrimitives.ReadInt32LittleEndian(rest),
                        BinaryPrimitives.ReadInt32LittleEndian(rest[4..]),
                        BinaryPrimitives.ReadInt32LittleEndian(rest[8..]),
                        BinaryPrimitives.ReadInt32LittleEndian(rest[12..]),
                    ]);
                    position += 16;
                    break;
                case Tag.Guid when rest.Length >= 16: value = new Guid(rest[..16]); position += 16; break;
                case Tag.String when TryReadLength(rest, out var length):
                    value = StrictUtf8.GetString(rest.Slice(4, length));
                    position += 4 + length;
                    break;
                case Tag.DateTime when rest.Length >= 8: value = DateTime.FromBinary(BinaryPrimitives.ReadInt64LittleEndian(rest)); position += 8; break;
                case Tag.DateTimeOffset when rest.Length >= 10:
                    value = new DateTimeOffset(
                        BinaryPrimitives.ReadInt64LittleEndian(rest),
                        TimeSpan.FromMinutes(BinaryPrimitives.ReadInt16LittleEndian(rest[8..])));
                    position += 10;
                    break;
                case Tag.DateOnly when rest.Length >= 4: value = DateOnly.FromDayNumber(BinaryPrimitives.ReadInt32LittleEndian(rest)); position += 4; break;
                case Tag.TimeOnly when rest.Length >= 8: value = new TimeOnly(BinaryPrimitives.ReadInt64LittleEndian(rest)); position += 8; break;
                case Tag.Bytes when TryReadLength(rest, out var length):
                    value = rest.Slice(4, length).ToArray();
                    position += 4 + length;
                    break;
                default:
                    return false;
            }
        }
        catch (ArgumentException)
        {
            return false; // a value out of its type's range (a date, a decimal's flags, an offset), or invalid UTF-8
        }

        return true;
    }

    // A declared length is accepted only when that many bytes follow it.
    private static bool TryReadLength(ReadOnlySpan<byte> rest, out int length)
    {
        length = 0;
        if (rest.Length < 4)
            return false;
        length = BinaryPrimitives.ReadInt32LittleEndian(rest);
        return length >= 0 && length <= rest.Length - 4;
    }
}
