# MicroKit.EntityFrameworkCore

EF Core helper extensions for persisting complex CLR types as JSON columns. Provides a `HasJsonConversion<T>()` fluent extension for `PropertyBuilder<T>` and a `JsonValueConverters` factory for creating `ValueConverter<T, string>` instances backed by `System.Text.Json`.

## When to use

Use this package when configuring EF Core entity mappings that store structured properties (collections, nested objects, value objects) as JSON strings in a single database column.

This package is distinct from `MicroKit.Data.EntityFrameworkCore`, which provides `IUnitOfWork` and repository infrastructure. Both can be referenced together.

## Installation

```
dotnet add package MicroKit.EntityFrameworkCore
```

## Key types

| Type | Description |
|---|---|
| `JsonPropertyBuilderExtensions.HasJsonConversion<T>()` | Configures a property to store its value as a JSON string with a structural value comparer so EF Core detects in-place mutations |
| `JsonValueConverters.Create<T>()` | Creates a `ValueConverter<T, string>` using `System.Text.Json`; throws on null deserialization |
| `JsonValueConverters.DefaultOptions` | Shared `JsonSerializerOptions`: camelCase, non-indented |

## Usage

```csharp
// In your IEntityTypeConfiguration<T>
public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        // Stores the Address value object as a JSON column
        builder.Property(o => o.ShippingAddress)
               .HasJsonConversion<Address>();

        // Stores a list of line-item value objects as JSON
        builder.Property(o => o.LineItems)
               .HasJsonConversion<List<LineItem>>();
    }
}
```

The extension attaches both a `ValueConverter<T, string>` and a structural `ValueComparer<T>` (based on round-trip JSON serialization) so that EF Core's change tracker correctly detects modifications to complex types.

## Dependencies

- `Microsoft.EntityFrameworkCore`
