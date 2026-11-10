namespace MicroKit.Persistence.Samples;

// README: "The domain model"
public sealed record OrderId : EntityId<OrderId>
{
    public OrderId(Guid value) : base(value) { }

    public static OrderId New() => new(Guid.CreateVersion7());
}

public sealed class Order : AuditableAggregateRoot<OrderId>
{
    private readonly List<OrderLine> _lines = [];
    private readonly List<TagLink> _tagLinks = [];

    // Used by EF Core: the audit fields are written through their backing fields.
    private Order(OrderId id, string number) : base(id, DateTimeOffset.UnixEpoch, Actor.Migration) => Number = number;

    private Order(OrderId id, string number, Actor placedBy, TimeProvider clock) : base(id, clock, placedBy) => Number = number;

    public string Number { get; private set; }

    public bool IsClosed { get; private set; }

    public IReadOnlyList<OrderLine> Lines => _lines;

    // Links to Tag, another aggregate: child entities of the order, loaded and versioned with it.
    public IReadOnlyList<TagLink> TagLinks => _tagLinks;

    public static Order Place(string number, Actor placedBy, TimeProvider clock) =>
        new(OrderId.New(), number, placedBy, clock);

    public void AddLine(string sku, int quantity, Actor by, TimeProvider clock) =>
        Mutate(clock.GetUtcNow(), by, () => _lines.Add(new OrderLine(Guid.CreateVersion7(), sku, quantity)));

    public void Close(Actor by, TimeProvider clock) => Mutate(clock.GetUtcNow(), by, () => IsClosed = true);

    public void Tag(TagId tag, Actor by, TimeProvider clock) =>
        Mutate(clock.GetUtcNow(), by, () => _tagLinks.Add(new TagLink(Id, tag)));

    public void Untag(TagId tag, Actor by, TimeProvider clock) =>
        Mutate(clock.GetUtcNow(), by, () => _tagLinks.RemoveAll(l => l.TagId == tag));
}

public sealed class OrderLine(Guid id, string sku, int quantity)
{
    public Guid Id { get; private set; } = id;

    public string Sku { get; private set; } = sku;

    public int Quantity { get; private set; } = quantity;
}

public sealed class TagLink(OrderId orderId, TagId tagId)
{
    public OrderId OrderId { get; private set; } = orderId;

    public TagId TagId { get; private set; } = tagId;
}

public sealed record TagId : EntityId<TagId>
{
    public TagId(Guid value) : base(value) { }
}

public sealed class Tag(TagId id, string name) : AggregateRoot<TagId>(id)
{
    public string Name { get; private set; } = name;
}
