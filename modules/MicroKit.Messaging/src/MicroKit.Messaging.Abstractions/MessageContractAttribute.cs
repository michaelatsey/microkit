using System.Globalization;

namespace MicroKit.Messaging;

/// <summary>
/// Declares the wire contract an <see cref="IIntegrationEvent"/> is published and consumed under:
/// a stable name and a version.
/// </summary>
/// <remarks>
/// <para>
/// <b>Mandatory on every published or consumed event.</b> The CLR type name is not usable as a
/// wire identity: a namespace rename invalidates messages already in flight, and a consumer in
/// another service holds a different type in a different assembly, so the name could never match.
/// A stable contract name is what lets a module be extracted into its own service without any
/// consumer noticing.
/// </para>
/// <para>
/// <b>The wire type is <c>&lt;name&gt;.v&lt;version&gt;</c></b> — see <see cref="WireType"/>. It
/// is what travels as the envelope's CloudEvents <c>type</c>, what the contract registry keys on,
/// and what the outbox records as a row's contract name. The version is declared separately so that
/// it cannot be forgotten and cannot be spelled two ways: a name that already ends in
/// <c>.v&lt;digits&gt;</c> is rejected, because it would produce <c>orders.placed.v1.v1</c>.
/// </para>
/// <para>
/// <b>Read on both sides.</b> A producer declares the contract it emits; a consuming module puts
/// the <i>same</i> name and version on its own local type, and that attribute is the whole of what
/// binds an arriving wire type to a CLR type it can deserialize into. Within one process a wire
/// type resolves to exactly one local type.
/// </para>
/// <para>
/// <b>Versioning is additive-only within a version.</b> An optional field keeps the version;
/// anything a consumer could break on takes a new <see cref="Version"/> and a period of dual
/// publication.
/// </para>
/// <code>
/// [MessageContract("saasbtp.safety.constat-recorded", Version = 2)]
/// public sealed record ConstatRecorded(Guid ConstatId, Guid SiteId) : IIntegrationEvent;
/// // wire type: "saasbtp.safety.constat-recorded.v2"
/// </code>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class MessageContractAttribute : Attribute
{
    private int _version = 1;

    /// <summary>Declares the contract name this event travels under.</summary>
    /// <param name="name">
    /// The contract name without its version, e.g. <c>saasbtp.safety.constat-recorded</c>. This is
    /// public API: renaming it strands every consumer subscribed to the old name.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is null, empty or whitespace, or already ends in
    /// <c>.v&lt;digits&gt;</c>.
    /// </exception>
    /// <remarks>
    /// The guard runs the moment the attribute is materialized, which is inside the registration
    /// call that names the offending type. The empty name is the case that matters: it is a usable
    /// dictionary key, so unguarded it would bind, resolve and travel.
    /// </remarks>
    public MessageContractAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (EndsWithVersionSuffix(name))
        {
            throw new ArgumentException(
                $"Contract name '{name}' already ends in a version suffix. Declare the name without " +
                "it and set Version instead — [MessageContract(\"orders.placed\", Version = 1)] — " +
                "so the wire type is built once, as '<name>.v<version>'.",
                nameof(name));
        }

        Name = name;
    }

    /// <summary>Gets the contract name, without its version.</summary>
    public string Name { get; }

    /// <summary>Gets or sets the contract version. Defaults to 1; must be 1 or greater.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is below 1.</exception>
    public int Version
    {
        get => _version;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            _version = value;
        }
    }

    /// <summary>
    /// Gets the wire type, <c>&lt;name&gt;.v&lt;version&gt;</c>: the CloudEvents <c>type</c> of
    /// every envelope carrying this contract.
    /// </summary>
    public string WireType => string.Create(CultureInfo.InvariantCulture, $"{Name}.v{Version}");

    private static bool EndsWithVersionSuffix(string name)
    {
        var dot = name.LastIndexOf(".v", StringComparison.Ordinal);
        if (dot < 0 || dot + 2 >= name.Length)
        {
            return false;
        }

        foreach (var c in name.AsSpan(dot + 2))
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
