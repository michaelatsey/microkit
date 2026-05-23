# Financial Domain Sample

This sample demonstrates financial domain modeling with emphasis on currency handling, precision, and audit requirements.

## Key Concepts

- **Multi-currency transactions**
- **Precision decimal arithmetic** 
- **Account balance management**
- **Transaction history tracking**
- **Audit trails with immutable events**

## Value Objects

```csharp
public sealed record Money(decimal Amount, string Currency) : IValueObject;
public sealed record AccountNumber(string Value) : IValueObject;
public sealed record TransactionId(Guid Value) : IEntityId;
```

## Domain Events

```csharp
public sealed record MoneyTransferredEvent(
    TransactionId TransactionId,
    AccountNumber FromAccount,
    AccountNumber ToAccount, 
    Money Amount,
    DateTimeOffset ProcessedAt) : DomainEvent;
```

## Performance Focus

- Zero-allocation money arithmetic operations
- Efficient decimal comparisons
- Optimized transaction event collection