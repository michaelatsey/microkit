namespace MicroKit.Messaging;

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
    private readonly List<MessagingBuilder> _builders = [];

    /// <summary>Gets the handler registry the declared handlers are written into.</summary>
    public MessageHandlerRegistry Handlers { get; } = new();

    /// <summary>Gets the published types, in declaration order, each once.</summary>
    public IReadOnlyList<Type> Publications => _publications;

    /// <summary>Gets every (handler, message type) pair declared, in declaration order, each once.</summary>
    public IReadOnlyList<HandledType> Handled => _handled;

    /// <summary>
    /// Gets the distinct non-null <c>bus.ServiceName</c> values set by the builders, in the order
    /// the builders were created. More than one is a composition error.
    /// </summary>
    public IReadOnlyList<string> ServiceNames =>
        [.. _builders.Select(b => b.ServiceName).OfType<string>().Distinct(StringComparer.Ordinal)];

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

    /// <summary>Records a builder, so its <c>ServiceName</c> is read when the options are built.</summary>
    /// <param name="builder">The builder of one <c>AddMicroKitMessaging</c> call.</param>
    public void Attach(MessagingBuilder builder) => _builders.Add(builder);

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

        // An attribute whose arguments its constructor rejects throws when it is materialized.
        // Recorded and reported at start with every other problem, so one bad type neither hides
        // the rest nor makes an assembly scan throw.
        try
        {
            _contracts[messageType] = MessageContracts.Find(messageType);
        }
        catch (ArgumentException ex)
        {
            _contracts[messageType] = null;
            _unreadable[messageType] = ex.Message;
        }
    }

    /// <summary>One handler and one message type it handles.</summary>
    /// <param name="HandlerType">The handler type.</param>
    /// <param name="MessageType">The handled message type.</param>
    internal readonly record struct HandledType(Type HandlerType, Type MessageType);
}
