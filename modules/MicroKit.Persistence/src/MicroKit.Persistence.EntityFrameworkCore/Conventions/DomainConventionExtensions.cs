using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using MicroKit.Domain.ValueObjects.Common;

namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>
/// Maps the types <c>MicroKit.Domain</c> defines — identifiers, <see cref="Actor"/>, audit
/// instants — so that no consumer writes these conversions by hand. Call them from
/// <see cref="DbContext.ConfigureConventions"/>.
/// </summary>
/// <example>
/// <code>
/// protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
///     => configurationBuilder.AddDomainConventions(typeof(Order).Assembly);
/// </code>
/// </example>
public static class DomainConventionExtensions
{
    /// <summary>
    /// Applies every MicroKit.Domain mapping: <see cref="AddEntityIdConventions"/>,
    /// <see cref="AddActorConvention"/> and <see cref="AddAuditInstantConvention"/>.
    /// </summary>
    /// <param name="builder">The model configuration builder passed to <c>ConfigureConventions</c>.</param>
    /// <param name="identifierAssemblies">The assemblies that declare <see cref="EntityId{T}"/> types.</param>
    /// <returns><paramref name="builder"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="builder"/> or <paramref name="identifierAssemblies"/>, or one of its elements, is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// An <see cref="EntityId{T}"/> type has no <c>(Guid)</c> constructor.
    /// </exception>
    /// <remarks>
    /// The set is these three conventions; a later version that adds one says so in its changelog.
    /// Any of them gives way to a conversion configured explicitly in <c>OnModelCreating</c>.
    /// </remarks>
    public static ModelConfigurationBuilder AddDomainConventions(
        this ModelConfigurationBuilder builder,
        params Assembly[] identifierAssemblies) =>
        builder
            .AddEntityIdConventions(identifierAssemblies)
            .AddActorConvention()
            .AddAuditInstantConvention();

    /// <summary>
    /// Maps every concrete type of <paramref name="identifierAssemblies"/> that derives from
    /// <see cref="EntityId{T}"/> of itself (<c>record OrderId : EntityId&lt;OrderId&gt;</c>), and
    /// <see cref="GuidId"/>, to a <see cref="Guid"/> column — keys, foreign keys and plain
    /// properties alike.
    /// </summary>
    /// <param name="builder">The model configuration builder passed to <c>ConfigureConventions</c>.</param>
    /// <param name="identifierAssemblies">The assemblies that declare <see cref="EntityId{T}"/> types.</param>
    /// <returns><paramref name="builder"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="builder"/> or <paramref name="identifierAssemblies"/>, or one of its elements, is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// An <see cref="EntityId{T}"/> type has no <c>(Guid)</c> constructor. Model building fails at
    /// once, naming the type, rather than on the first row read.
    /// </exception>
    /// <remarks>
    /// <para>
    /// A value is read back through the type's own <c>(Guid)</c> constructor, public or not, so the
    /// <see cref="EntityId{T}"/> validation (no empty identifier) applies to stored rows too.
    /// </para>
    /// <para>
    /// The conventions do not change value generation. EF Core generates a key only when its CLR
    /// type is an integer or <see cref="Guid"/>; an identifier type is neither, so its keys are
    /// <c>ValueGenerated.Never</c>: identifiers are assigned by the domain.
    /// </para>
    /// <para>
    /// A conversion configured explicitly in <c>OnModelCreating</c> takes precedence. An identifier
    /// type from an assembly that is not passed is not mapped, nor is a generic identifier type or a
    /// type deriving from another identifier; with no assembly, only <see cref="GuidId"/> is.
    /// </para>
    /// <para>
    /// Compiled models (<c>dotnet ef dbcontext optimize</c>) and NativeAOT precompiled queries are not
    /// supported by these conventions: the generated code instantiates their converters, which are
    /// internal, and does not compile.
    /// </para>
    /// </remarks>
    [UnconditionalSuppressMessage(
        "Trimming", "IL2026",
        Justification = "Identifier types are found by reflection over the assemblies the caller names; EF Core is not trim-safe itself (see IL2091 in the project file).")]
    [UnconditionalSuppressMessage(
        "Trimming", "IL2055",
        Justification = "EntityIdConverter<T> is closed over identifier types found in the caller's assemblies.")]
    [UnconditionalSuppressMessage(
        "Trimming", "IL2076",
        Justification = "EntityIdConverter<T> is closed over identifier types found in the caller's assemblies; their constructors are reflected at run time.")]
    [UnconditionalSuppressMessage(
        "Trimming", "IL2072",
        Justification = "Identifier constructors are looked up by reflection; EF Core is not trim-safe itself.")]
    [UnconditionalSuppressMessage(
        "AOT", "IL3050",
        Justification = "EntityIdConverter<T> is a reference-type instantiation; EF Core is not AOT-safe itself.")]
    public static ModelConfigurationBuilder AddEntityIdConventions(
        this ModelConfigurationBuilder builder,
        params Assembly[] identifierAssemblies)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(identifierAssemblies);
        if (Array.Exists(identifierAssemblies, assembly => assembly is null))
            throw new ArgumentNullException(nameof(identifierAssemblies), "An assembly in the list is null.");

        builder.Properties<GuidId>().HaveConversion<GuidIdConverter>();

        foreach (var identifierType in identifierAssemblies.Distinct().SelectMany(EntityIdTypesIn))
        {
            if (IdentifierConstructor.Find(identifierType) is null)
                throw new InvalidOperationException(IdentifierConstructor.MissingMessage(identifierType));

            var converterType = typeof(EntityIdConverter<>).MakeGenericType(identifierType);

            builder.Properties(identifierType).HaveConversion(converterType);
        }

        return builder;
    }

    /// <summary>
    /// Maps every <see cref="Actor"/> property to a string column of at most
    /// <see cref="Actor.MaxLength"/> characters, read back through <see cref="Actor.Restore(string)"/>:
    /// the built-in actors (<see cref="Actor.System"/>, <see cref="Actor.Anonymous"/>,
    /// <see cref="Actor.Migration"/>) come back as their shared instances.
    /// </summary>
    /// <param name="builder">The model configuration builder passed to <c>ConfigureConventions</c>.</param>
    /// <returns><paramref name="builder"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// A conversion or a maximum length configured explicitly in <c>OnModelCreating</c> takes
    /// precedence. On an existing schema the maximum length is a column change: plan its migration.
    /// Compiled models and NativeAOT precompiled queries are not supported (the converter is internal).
    /// </remarks>
    public static ModelConfigurationBuilder AddActorConvention(this ModelConfigurationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Properties<Actor>().HaveConversion<ActorConverter>().HaveMaxLength(Actor.MaxLength);
        return builder;
    }

    /// <summary>
    /// Normalises the <c>CreatedAt</c> and <c>UpdatedAt</c> instants of every
    /// <see cref="IAuditableEntity"/> to offset zero when they are read and written, and rejects a
    /// stored <c>default(DateTimeOffset)</c> — the rules the <c>Auditable*</c> constructors enforce,
    /// which materialisation through backing fields otherwise bypasses.
    /// </summary>
    /// <param name="builder">The model configuration builder passed to <c>ConfigureConventions</c>.</param>
    /// <returns><paramref name="builder"/> for chaining.</returns>
    /// <remarks>
    /// <para>
    /// A stored row with a non-zero offset (SQL Server <c>datetimeoffset</c>, SQLite text) is read
    /// back at offset zero, so a later write to a PostgreSQL <c>timestamptz</c> column, which only
    /// accepts offset zero, succeeds. A stored <c>0001-01-01</c> fails the read with an
    /// <see cref="InvalidOperationException"/>.
    /// </para>
    /// <para>
    /// A query parameter compared with those properties is normalised too, and never rejected:
    /// <c>CreatedAt &gt;= DateTimeOffset.MinValue</c> runs. A rejected read surfaces as EF Core's
    /// "error reading a database value" <see cref="InvalidOperationException"/>, wrapping this
    /// convention's message.
    /// </para>
    /// <para>
    /// Only those two properties of auditable entities are converted; other
    /// <see cref="DateTimeOffset"/> properties keep their offset. A converter configured for those
    /// properties — an explicit <c>HasConversion</c>, or a pre-convention
    /// <c>Properties&lt;DateTimeOffset&gt;().HaveConversion(...)</c> — takes precedence and turns this
    /// normalisation off for them.
    /// </para>
    /// <para>
    /// Compiled models (<c>dotnet ef dbcontext optimize</c>) and NativeAOT precompiled queries are not
    /// supported by this convention: the generated code calls internal members and does not compile.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static ModelConfigurationBuilder AddAuditInstantConvention(this ModelConfigurationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Conventions.Add(static _ => new AuditInstantConvention());
        return builder;
    }

    [UnconditionalSuppressMessage(
        "Trimming", "IL2026",
        Justification = "Identifier types are found by reflection over the assemblies the caller names; EF Core is not trim-safe itself.")]
    private static IEnumerable<Type> EntityIdTypesIn(Assembly assembly)
    {
        Type?[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types; // scan what loaded; a type that failed to load cannot be mapped anyway
        }

        return types.OfType<Type>().Where(type => !type.IsAbstract && !type.IsGenericTypeDefinition && IsEntityId(type));
    }

    private static bool IsEntityId(Type type)
    {
        try
        {
            for (var current = type.BaseType; current is not null; current = current.BaseType)
            {
                if (current.IsGenericType
                    && current.GetGenericTypeDefinition() == typeof(EntityId<>)
                    && current.GetGenericArguments()[0] == type)
                {
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is TypeLoadException or FileNotFoundException or FileLoadException)
        {
            // A base type that cannot load is not EntityId<T>, which MicroKit.Domain provides.
        }

        return false;
    }
}
