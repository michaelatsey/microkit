using System.Text.Json;

namespace MicroKit.Result;

/// <summary>
/// Lets <see cref="Result{T}"/> write itself as the JSON envelope, through a converter that does not know its value
/// type: its <c>T</c> stays static, so no reflection is needed (<see cref="Serialization.ResultEnvelopeJsonConverterFactory"/>).
/// </summary>
internal interface IResultEnvelope
{
    /// <summary>Writes the envelope, never <see cref="IError.Metadata"/>, and never the message of a fault of the system.</summary>
    void WriteJson(Utf8JsonWriter writer, JsonSerializerOptions options);
}
