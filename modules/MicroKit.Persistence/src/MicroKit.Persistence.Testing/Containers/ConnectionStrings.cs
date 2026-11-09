using System.Data.Common;

namespace MicroKit.Persistence.Testing;

/// <summary>Connection-string edits shared by the relational fixtures, provider-neutral (BCL only).</summary>
internal static class ConnectionStrings
{
    /// <summary>
    /// Returns <paramref name="connectionString"/> pointing at <paramref name="database"/>: the
    /// <c>Initial Catalog</c> synonym is removed and <c>Database</c> set, the value quoted as the providers'
    /// builders (which derive from <see cref="DbConnectionStringBuilder"/>) read it back.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="database"/> is null, empty or white space.</exception>
    internal static string WithDatabase(string connectionString, string database)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(database);

        var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
        builder.Remove("Initial Catalog");
        builder["Database"] = database;
        return builder.ConnectionString;
    }
}
