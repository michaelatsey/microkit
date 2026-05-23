# Identity & Authentication Domain Sample

This sample demonstrates user identity management with modern security patterns and audit capabilities.

## Key Concepts

- **User registration and profile management**
- **Email verification workflows**
- **Password policy enforcement**
- **Role-based access control**
- **Security event tracking**

## Value Objects

```csharp
public sealed record Email(string Value) : IValueObject;
public sealed record FullName(string FirstName, string LastName) : IValueObject;
public sealed record PhoneNumber(string Value) : IValueObject;
```

## Aggregates

```csharp
public sealed class User : AuditableAggregateRoot<UserId>
{
    public static User Register(Email email, FullName fullName);
    public void VerifyEmail(string verificationToken);
    public void ChangePassword(string newPassword);
}
```

## Domain Events

```csharp
public sealed record UserRegisteredEvent(
    UserId UserId,
    Email Email,
    FullName FullName,
    DateTimeOffset RegisteredAt) : DomainEvent;

public sealed record EmailVerifiedEvent(
    UserId UserId,
    Email Email,
    DateTimeOffset VerifiedAt) : DomainEvent;
```

## Security Patterns

- Immutable security events for audit trails
- Value object validation for email/phone formats
- Business rules for password complexity