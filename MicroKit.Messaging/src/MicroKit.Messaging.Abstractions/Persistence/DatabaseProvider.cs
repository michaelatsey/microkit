namespace MicroKit.Messaging.Abstractions.Persistence;

/// <summary>Identifies the relational database engine used for messaging persistence.</summary>
public enum DatabaseProvider
{
    /// <summary>Microsoft SQL Server.</summary>
    SqlServer,

    /// <summary>PostgreSQL.</summary>
    PostgreSql
}
