# Subscription & Recurring Billing Domain Sample

This sample demonstrates subscription lifecycle management with complex billing scenarios and temporal domain logic.

## Key Concepts

- **Subscription lifecycle management**
- **Recurring billing calculations**
- **Prorated charges and refunds**  
- **Plan upgrades and downgrades**
- **Billing cycle management**

## Value Objects

```csharp
public sealed record Money(decimal Amount, string Currency) : IValueObject;
public sealed record DateRange(DateTimeOffset Start, DateTimeOffset End) : IValueObject;
public sealed record Percentage(decimal Value) : IValueObject;
```

## Aggregates

```csharp
public sealed class Subscription : AggregateRoot<SubscriptionId>
{
    public static Subscription Create(CustomerId customerId, PlanId planId);
    public void ChangePlan(PlanId newPlanId, DateTimeOffset effectiveDate);
    public void Cancel(DateTimeOffset cancellationDate);
    public Money CalculateProration(DateRange billingPeriod);
}
```

## Domain Events

```csharp
public sealed record SubscriptionCreatedEvent(
    SubscriptionId SubscriptionId,
    CustomerId CustomerId,
    PlanId PlanId,
    DateTimeOffset CreatedAt) : DomainEvent;

public sealed record PlanChangedEvent(
    SubscriptionId SubscriptionId,
    PlanId FromPlan,
    PlanId ToPlan,
    Money ProrationAmount,
    DateTimeOffset EffectiveDate) : DomainEvent;
```

## Complex Calculations

- Date range operations for billing periods
- Percentage-based discount calculations  
- Multi-currency pricing with conversion
- Temporal business rules for plan changes