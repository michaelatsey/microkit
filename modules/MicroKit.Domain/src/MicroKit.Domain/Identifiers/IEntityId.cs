namespace MicroKit.Domain.Identifiers;

/// <summary>
/// Marker interface for all strongly-typed entity identifiers.
/// Ensures type safety and prevents primitive obsession in domain models.
/// </summary>
/// <remarks>
/// The interface carries no member: it is a generic constraint only. Each identifier type exposes
/// its own typed value, such as <see cref="GuidId.Value"/>, so reading it never boxes and never
/// loses the type.
/// </remarks>
public interface IEntityId;
