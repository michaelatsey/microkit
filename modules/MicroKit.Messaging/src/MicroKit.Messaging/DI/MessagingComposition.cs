namespace MicroKit.Messaging;

using System.Reflection;
using MicroKit.Messaging.Bus;
using MicroKit.Messaging.Registry;

/// <summary>
/// What the <see cref="MessagingBuilder"/> calls declared: the publications, the handlers and the
/// service name. One instance per service collection, shared by every
/// <c>AddMicroKitMessaging</c> call, so repeated calls add to one composition rather than
/// replacing each other.
/// </summary>
/// <remarks>
/// Each message type's <see cref="MessageContractAttribute"/> is read here, once, when the type is
/// declared. The hot paths read the contract from <c>IntegrationEventRegistry</c>, never by
/// reflection.
/// </remarks>
internal sealed class MessagingComposition
{
    private readonly List<Type> _publications = [];
    private readonly List<HandledType> _handled = [];
    private readonly Dictionary<Type, MessageContractAttribute?> _contracts = [];
    private readonly Dictionary<Type, string> _unreadable = [];
    // One slot per AddMicroKitMessaging call, holding that call's bus.ServiceName. The names, not
    // the builders: a builder holds the IServiceCollection, and this singleton outlives it.
    private readonly List<string?> _serviceNames = [];
    private readonly List<UnloadedTypes> _unloaded = [];

    /// <summary>Gets the handler registry the declared handlers are written into.</summary>
    public MessageHandlerRegistry Handlers { get; } = new();

    /// <summary>Gets the published types, in declaration order, each once.</summary>
    public IReadOnlyList<Type> Publications => _publications;

    /// <summary>Gets every (handler, message type) pair declared, in declaration order, each once.</summary>
    public IReadOnlyList<HandledType> Handled => _handled;

    /// <summary>
    /// Gets the distinct non-null <c>bus.ServiceName</c> values set by the
    /// <c>AddMicroKitMessaging</c> calls, in call order. More than one is a composition error.
    /// </summary>
    public IReadOnlyList<string> ServiceNames =>
        [.. _serviceNames.OfType<string>().Distinct(StringComparer.Ordinal)];

    /// <summary>Gets the contract a declared type carries, or null when it carries none.</summary>
    /// <param name="type">A type declared as a publication or handled by a handler.</param>
    /// <returns>The attribute read at declaration, or <see langword="null"/>.</returns>
    public MessageContractAttribute? ContractOf(Type type) => _contracts[type];

    /// <summary>
    /// Gets why a declared type's <see cref="MessageContractAttribute"/> could not be read — a
    /// blank name, a name carrying its own version suffix — or null when it was read or is absent.
    /// </summary>
    /// <param name="type">A declared type.</param>
    /// <returns>The attribute's own error, or <see langword="null"/>.</returns>
    public string? UnreadableContractOf(Type type) => _unreadable.GetValueOrDefault(type);

    /// <summary>
    /// Gets the assemblies a handler scan could load only in part, with what failed. Reported at
    /// start as one <c>Warning</c> per assembly; never a failure, since a type that did not load
    /// is not necessarily a handler.
    /// </summary>
    public IReadOnlyList<UnloadedTypes> Unloaded => _unloaded;

    /// <summary>Records that a handler scan of <paramref name="assembly"/> could not load every type.</summary>
    /// <param name="assembly">The assembly's name.</param>
    /// <param name="exception">What <c>GetTypes()</c> threw.</param>
    public void RecordUnloadedTypes(string assembly, ReflectionTypeLoadException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var loaderExceptions = exception.LoaderExceptions.OfType<Exception>().ToList();
        var failed = exception.Types.Count(t => t is null);
        var names = loaderExceptions.OfType<TypeLoadException>()
            .Select(e => e.TypeName)
            .Where(n => !string.IsNullOrEmpty(n))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        _unloaded.Add(new UnloadedTypes(
            assembly,
            failed,
            names,
            loaderExceptions.Count > 0 ? loaderExceptions[0].Message : exception.Message));
    }

    /// <summary>Opens the service-name slot of one <c>AddMicroKitMessaging</c> call.</summary>
    /// <returns>The slot, for <see cref="SetServiceName"/>.</returns>
    public int OpenServiceNameSlot()
    {
        _serviceNames.Add(null);
        return _serviceNames.Count - 1;
    }

    /// <summary>
    /// Records one call's <c>bus.ServiceName</c>. Read when the options are first built, so a name
    /// set on a builder after <c>AddMicroKitMessaging</c> returned still counts.
    /// </summary>
    /// <param name="slot">The call's slot.</param>
    /// <param name="serviceName">The name, or null to leave it unset.</param>
    public void SetServiceName(int slot, string? serviceName) => _serviceNames[slot] = serviceName;

    /// <summary>Declares a publication. Declaring one type twice is a no-op.</summary>
    /// <param name="messageType">The published type.</param>
    public void AddPublication(Type messageType)
    {
        Read(messageType);
        if (!_publications.Contains(messageType))
        {
            _publications.Add(messageType);
        }
    }

    /// <summary>
    /// Declares that <paramref name="handlerType"/> handles <paramref name="messageType"/>, and
    /// registers it in <see cref="Handlers"/>. Declaring one pair twice is a no-op.
    /// </summary>
    /// <param name="handlerType">The handler type.</param>
    /// <param name="messageType">The message type, from a closed <c>IMessageHandler&lt;T&gt;</c>.</param>
    public void AddHandler(Type handlerType, Type messageType)
    {
        Read(messageType);
        var pair = new HandledType(handlerType, messageType);
        if (_handled.Contains(pair))
        {
            return;
        }

        _handled.Add(pair);
        Handlers.Register(messageType, handlerType.AssemblyQualifiedName!, handlerType);
    }

    private void Read(Type messageType)
    {
        if (_contracts.ContainsKey(messageType))
        {
            return;
        }

        // An attribute whose arguments it rejects throws when it is materialized: its constructor
        // throws ArgumentException directly (a blank name, a version suffix in the name), while a
        // rejected PROPERTY — Version below 1 — reaches us wrapped by reflection in a
        // CustomAttributeFormatException. Both are recorded and reported at start with every other
        // problem, so one bad type neither hides the rest nor makes an assembly scan throw.
        try
        {
            _contracts[messageType] = MessageContracts.Find(messageType);
        }
        catch (Exception ex) when (ex is ArgumentException or CustomAttributeFormatException)
        {
            _contracts[messageType] = null;
            _unreadable[messageType] = Innermost(ex).Message;
        }
    }

    private static Exception Innermost(Exception exception)
    {
        while (exception.InnerException is { } inner)
        {
            exception = inner;
        }

        return exception;
    }

    /// <summary>What a handler scan could not load from one assembly.</summary>
    /// <param name="Assembly">The assembly's name.</param>
    /// <param name="Count">How many types failed to load.</param>
    /// <param name="TypeNames">The names the loader reported, when it reported any.</param>
    /// <param name="FirstError">The first loader exception's message.</param>
    internal sealed record UnloadedTypes(string Assembly, int Count, IReadOnlyList<string> TypeNames, string FirstError);

    /// <summary>One handler and one message type it handles.</summary>
    /// <param name="HandlerType">The handler type.</param>
    /// <param name="MessageType">The handled message type.</param>
    internal readonly record struct HandledType(Type HandlerType, Type MessageType);
}
