namespace MicroKit.Messaging.Serialization;

using System.Text.Json;

/// <summary>
/// The System.Text.Json options of the default serializer, configured by the host through
/// <see cref="MessagingBuilder.ConfigureJson"/> and read through <c>IOptions</c>.
/// </summary>
/// <remarks>
/// A wrapper rather than <c>IOptions&lt;JsonSerializerOptions&gt;</c> directly: an unnamed
/// <c>JsonSerializerOptions</c> option would be shared with anything else in the host that
/// configures one, and its default instance is not the Web defaults this module starts from.
/// </remarks>
internal sealed record MessagingJsonOptions
{
    /// <summary>Gets the options, starting from <see cref="JsonSerializerDefaults.Web"/>.</summary>
    public JsonSerializerOptions SerializerOptions { get; } = new(JsonSerializerDefaults.Web);
}
