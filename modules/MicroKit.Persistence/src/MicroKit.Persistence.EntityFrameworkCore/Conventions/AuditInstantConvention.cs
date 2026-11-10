using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>
/// Sets <see cref="UtcInstantConverter"/> on the <c>CreatedAt</c> and <c>UpdatedAt</c> properties
/// of every entity type implementing <see cref="IAuditableEntity"/>, as each property is added.
/// </summary>
/// <remarks>
/// An interactive convention rather than a finalizing one: the converter must be in place before
/// the provider's type-mapping conventions run. It is set at convention level, so an explicit
/// <c>HasConversion</c> on the property wins.
/// </remarks>
internal sealed class AuditInstantConvention : IPropertyAddedConvention
{
    // Converters are immutable: one instance serves every audit property.
    private static readonly UtcInstantConverter s_converter = new();

    public void ProcessPropertyAdded(
        IConventionPropertyBuilder propertyBuilder,
        IConventionContext<IConventionPropertyBuilder> context)
    {
        // The declaring CLR type — entity, owned or complex — is what carries the audit contract.
        var property = propertyBuilder.Metadata;
        if (!typeof(IAuditableEntity).IsAssignableFrom(property.DeclaringType.ClrType))
            return;

        if (property.Name is not (nameof(IAuditableEntity.CreatedAt) or nameof(IAuditableEntity.UpdatedAt))
            || (property.ClrType != typeof(DateTimeOffset) && property.ClrType != typeof(DateTimeOffset?)))
        {
            return;
        }

        propertyBuilder.HasConversion(s_converter);
    }
}
