using System.Text;

namespace MicroKit.Persistence;

using MicroKit.Result;

/// <summary>
/// The errors of the <see cref="IResultUnitOfWork"/> path: persistence conflicts, of category
/// <see cref="ErrorCategory.Conflict"/>, coded <c>PERSISTENCE.&lt;ROOT&gt;.&lt;REASON&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>&lt;ROOT&gt;</c> is the error name of the failing entity's aggregate root: by default the simple CLR
/// name of the root in UPPER_SNAKE (<see cref="DefaultErrorName"/>). When the failing entities belong to
/// more than one aggregate, or to none, the code is <c>PERSISTENCE.&lt;REASON&gt;</c>: a root is never
/// guessed.
/// </para>
/// <para>
/// The messages are fixed and client-safe: a table, constraint or column name never appears in a code or
/// a message. The original exception stays in process: EF Core logs a failed save
/// (<c>SaveChangesFailed</c>); a version conflict a repository detects itself is not logged by the
/// conversion.
/// </para>
/// </remarks>
public static class PersistenceErrors
{
    /// <summary>The first segment of every code.</summary>
    public const string Prefix = "PERSISTENCE";

    /// <summary>The reason of a unique-constraint violation.</summary>
    public const string DuplicateReason = "DUPLICATE";

    /// <summary>The reason of a foreign-key violation.</summary>
    public const string ReferenceReason = "REFERENCE";

    /// <summary>The reason of an optimistic concurrency conflict.</summary>
    public const string ConcurrencyReason = "CONCURRENCY";

    private const string DuplicateMessage =
        "The change conflicts with existing data: a value that must be unique is already used.";

    private const string ReferenceMessage =
        "The change references data that does not exist, or removes data that other data still references.";

    private const string ConcurrencyMessage =
        "The data was changed by another operation since it was read; reload it and try again.";

    /// <summary>Creates the error of a unique-constraint violation.</summary>
    /// <param name="errorName">The error name of the aggregate root, or <see langword="null"/> when there is no single root.</param>
    /// <returns><c>PERSISTENCE.&lt;ROOT&gt;.DUPLICATE</c>, or <c>PERSISTENCE.DUPLICATE</c>.</returns>
    /// <exception cref="ArgumentException"><paramref name="errorName"/> is not a valid error name (<see cref="IsValidErrorName"/>).</exception>
    public static CategorizedError Duplicate(string? errorName = null) =>
        Create(errorName, DuplicateReason, DuplicateMessage);

    /// <summary>Creates the error of a foreign-key violation.</summary>
    /// <param name="errorName">The error name of the aggregate root, or <see langword="null"/> when there is no single root.</param>
    /// <returns><c>PERSISTENCE.&lt;ROOT&gt;.REFERENCE</c>, or <c>PERSISTENCE.REFERENCE</c>.</returns>
    /// <exception cref="ArgumentException"><paramref name="errorName"/> is not a valid error name (<see cref="IsValidErrorName"/>).</exception>
    public static CategorizedError Reference(string? errorName = null) =>
        Create(errorName, ReferenceReason, ReferenceMessage);

    /// <summary>Creates the error of an optimistic concurrency conflict.</summary>
    /// <param name="errorName">The error name of the aggregate root, or <see langword="null"/> when there is no single root.</param>
    /// <returns><c>PERSISTENCE.&lt;ROOT&gt;.CONCURRENCY</c>, or <c>PERSISTENCE.CONCURRENCY</c>.</returns>
    /// <exception cref="ArgumentException"><paramref name="errorName"/> is not a valid error name (<see cref="IsValidErrorName"/>).</exception>
    public static CategorizedError Concurrency(string? errorName = null) =>
        Create(errorName, ConcurrencyReason, ConcurrencyMessage);

    /// <summary>
    /// Gets the default error name of an aggregate root: its simple CLR name in UPPER_SNAKE, without
    /// generic arity (<c>OrderLine</c> → <c>ORDER_LINE</c>, <c>HTTPRequest</c> → <c>HTTP_REQUEST</c>).
    /// </summary>
    /// <param name="rootType">The aggregate root's CLR type.</param>
    /// <returns>The name; it is not valid (<see cref="IsValidErrorName"/>) when the CLR name has characters outside A–Z, 0–9 and '_'.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rootType"/> is <see langword="null"/>.</exception>
    public static string DefaultErrorName(Type rootType)
    {
        ArgumentNullException.ThrowIfNull(rootType);

        var name = rootType.Name;
        var arity = name.IndexOf('`', StringComparison.Ordinal);
        if (arity >= 0)
            name = name[..arity];

        var builder = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var current = name[i];
            if (i > 0 && char.IsUpper(current))
            {
                var previous = name[i - 1];
                var nextIsLower = i + 1 < name.Length && char.IsLower(name[i + 1]);
                if (char.IsLower(previous) || char.IsDigit(previous) || (char.IsUpper(previous) && nextIsLower))
                    builder.Append('_');
            }

            builder.Append(char.ToUpperInvariant(current));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Tells whether <paramref name="name"/> is a valid error name: UPPER_SNAKE, starting with a letter,
    /// made of A–Z, 0–9 and single underscores between words (<c>ORDER</c>, <c>SHOP_ORDER</c>, <c>ORDER2</c>).
    /// </summary>
    /// <param name="name">The candidate name.</param>
    /// <returns><see langword="true"/> when the name can be a code segment.</returns>
    public static bool IsValidErrorName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name[0] is < 'A' or > 'Z' || name[^1] == '_')
            return false;

        for (var i = 1; i < name.Length; i++)
        {
            var c = name[i];
            if (c == '_')
            {
                if (name[i - 1] == '_')
                    return false;
            }
            else if (c is not (>= 'A' and <= 'Z') and not (>= '0' and <= '9'))
            {
                return false;
            }
        }

        return true;
    }

    private static CategorizedError Create(string? errorName, string reason, string message)
    {
        if (errorName is not null && !IsValidErrorName(errorName))
        {
            throw new ArgumentException(
                $"'{errorName}' is not a valid error name: use UPPER_SNAKE (A–Z, 0–9, single underscores), starting with a letter.",
                nameof(errorName));
        }

        var code = errorName is null ? $"{Prefix}.{reason}" : $"{Prefix}.{errorName}.{reason}";
        return Error.Conflict(ErrorCode.From(code), message);
    }
}
