using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MicroKit.Result.Serialization;

/// <summary>
/// Writes and reads the <c>error</c> object of the result converters, without reflection:
/// <c>{"code","message","category","severity","propertyName"?,"errors"?,"metadata"?}</c>.
/// </summary>
internal static class ErrorJson
{
    // ResultJsonView.Full is for in-process storage, never a response (ADR-RESULT-001 D12): only then is every message,
    // error and metadata written. The Client view may reach a client, which reads what ToHttpResult shows it.
    internal static void Write(Utf8JsonWriter writer, IError error, ResultJsonView view)
    {
        if (string.IsNullOrWhiteSpace(error.Code.Value))
            throw new JsonException($"Cannot serialize an error of type {error.GetType().Name} with an uninitialized code.");

        // For a client (the Client view), the error says what ToHttpResult's problem details say: a collection whose
        // category does not describe the input names only its errors of that category, nested collections flattened,
        // and its message joins theirs; a field is named only by a Validation or BusinessRule error.
        var forClient = view == ResultJsonView.Client;
        List<IError>? named = null;
        var message = error.Message;
        var severity = error.Severity;
        var redacted = forClient && error.Category.IsSystemFault();
        if (forClient && error is ErrorCollection filtered && !filtered.Category.DescribesInput())
        {
            // One pass over the leaves: the errors of the collection's category (never none: the category is a
            // leaf's), their highest severity and, unless redacted, their joined messages.
            var category = filtered.Category;
            named = [];
            var joined = redacted ? null : new StringBuilder();
            foreach (var leaf in filtered.Flatten())
            {
                if (leaf.Category != category)
                    continue;
                if (named.Count == 0 || leaf.Severity > severity)
                    severity = leaf.Severity;
                joined?.Append(named.Count == 0 ? "" : "; ").Append(leaf.Message);
                named.Add(leaf);
            }

            if (joined is not null)
                message = joined.ToString();
        }

        if (redacted)
            message = ErrorCategoryExtensions.SystemFaultMessage;

        writer.WriteStartObject();
        writer.WriteString("code", error.Code.Value);
        writer.WriteString("message", message);
        writer.WriteString("category", error.Category.ToString());
        writer.WriteString("severity", severity.ToString());

        if (error is IValidationError validation && (!forClient || error.Category.DescribesInput()))
            writer.WriteString("propertyName", validation.PropertyName);

        if (error is ErrorCollection collection)
        {
            writer.WriteStartArray("errors");
            foreach (var inner in (IEnumerable<IError>?)named ?? collection)
                Write(writer, inner, view);
            writer.WriteEndArray();
        }

        // Metadata is in-process context, never sent to a client by default (ADR-RESULT-001 D1, D12).
        if (view == ResultJsonView.Full && error.Metadata.Count > 0)
        {
            writer.WriteStartObject("metadata");
            foreach (var pair in error.Metadata)
            {
                writer.WritePropertyName(pair.Key);
                WritePrimitive(writer, pair.Value);
            }
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    internal static IError Read(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected an error object.");

        string? code = null;
        string? message = null;
        string? category = null;
        string? severity = null;
        string? propertyName = null;
        List<IError>? errors = null;
        IReadOnlyDictionary<string, object?>? metadata = null;
        HashSet<string>? seen = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Expected a property name in the error object.");

            var name = reader.GetString()!; // Safe: the token is a property name
            seen ??= new HashSet<string>(StringComparer.Ordinal);
            if (!seen.Add(name))
                throw new JsonException($"Duplicate '{name}' property in the error object.");
            reader.Read();
            switch (name)
            {
                case "code": code = reader.GetString(); break;
                case "message": message = reader.GetString(); break;
                case "category": category = reader.GetString(); break;
                case "severity": severity = reader.GetString(); break;
                case "propertyName": propertyName = reader.GetString(); break;
                case "errors": errors = ReadErrors(ref reader); break;
                case "metadata": metadata = ReadMetadata(ref reader); break;
                default: reader.Skip(); break;
            }
        }

        // Every error object carries its code, a collection's included: a missing, null or empty code does not
        // describe a valid error.
        if (string.IsNullOrWhiteSpace(code))
            throw new JsonException("An error requires a non-empty 'code' property.");

        if (errors is not null)
        {
            if (errors.Count == 0)
                throw new JsonException("An error collection cannot be empty.");
            return ErrorCollection.From(errors);
        }

        var errorCode = ErrorCode.From(code);

        var parsedCategory = Enum.TryParse<ErrorCategory>(category, ignoreCase: true, out var c) && Enum.IsDefined(c)
            ? c
            : ErrorCategory.Technical;
        var parsedSeverity = Enum.TryParse<ErrorSeverity>(severity, ignoreCase: true, out var s) && Enum.IsDefined(s)
            ? s
            : ErrorSeverity.Error;
        IReadOnlyDictionary<string, object?> parsedMetadata = metadata ?? (IReadOnlyDictionary<string, object?>)ErrorMetadata.Empty;
        var text = message ?? "Unknown error";

        return propertyName is null
            ? new DeserializedError(errorCode, text, parsedCategory, parsedSeverity, parsedMetadata)
            : new DeserializedValidationError(errorCode, text, parsedCategory, parsedSeverity, parsedMetadata, propertyName);
    }

    private static List<IError> ReadErrors(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Expected an array of errors.");
        var errors = new List<IError>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            errors.Add(Read(ref reader));
        return errors;
    }

    private static ImmutableDictionary<string, object?> ReadMetadata(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected a metadata object.");
        var metadata = new Dictionary<string, object?>(StringComparer.Ordinal);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var key = reader.GetString()!; // Safe: a property name inside an object, checked by the reader
            if (metadata.ContainsKey(key))
                throw new JsonException($"Duplicate '{key}' key in the error metadata.");
            reader.Read();
            metadata[key] = reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.Number => ReadNumber(ref reader),
                JsonTokenType.True => true,
                JsonTokenType.False => false,
                JsonTokenType.Null => null,
                _ => JsonElement.ParseValue(ref reader),
            };
        }
        return metadata.ToImmutableDictionary(StringComparer.Ordinal); // errors are immutable, as ErrorMetadata's are
    }

    private static object ReadNumber(ref Utf8JsonReader reader)
    {
        if (reader.TryGetInt64(out var integer))
            return integer;
        if (reader.TryGetUInt64(out var unsigned))
            return unsigned;
        return reader.GetDouble(); // a decimal beyond double precision loses digits: metadata is not a ledger
    }

    private static void WritePrimitive(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null: writer.WriteNullValue(); break;
            case string text: writer.WriteStringValue(text); break;
            case bool flag: writer.WriteBooleanValue(flag); break;
            case int number: writer.WriteNumberValue(number); break;
            case long number: writer.WriteNumberValue(number); break;
            case short number: writer.WriteNumberValue(number); break;
            case byte number: writer.WriteNumberValue(number); break;
            case uint number: writer.WriteNumberValue(number); break;
            case ulong number: writer.WriteNumberValue(number); break;
            case decimal number: writer.WriteNumberValue(number); break;
            case double number when double.IsFinite(number): writer.WriteNumberValue(number); break;
            case float number when float.IsFinite(number): writer.WriteNumberValue(number); break;
            case Guid id: writer.WriteStringValue(id); break;
            case DateTimeOffset instant: writer.WriteStringValue(instant); break;
            case DateTime instant: writer.WriteStringValue(instant); break;
            case JsonElement element: element.WriteTo(writer); break;
            // Any other value is written as its invariant string: no reflection, so the converters stay AOT-safe.
            default: writer.WriteStringValue(Convert.ToString(value, CultureInfo.InvariantCulture)); break;
        }
    }
}
