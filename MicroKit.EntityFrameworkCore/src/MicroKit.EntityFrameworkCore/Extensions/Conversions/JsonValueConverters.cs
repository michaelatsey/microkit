using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace MicroKit.EntityFrameworkCore.Extensions.Conversions;

public class JsonValueConverters
{
    public static ValueConverter<T, string> Create<T>(
        JsonSerializerOptions? options = null)
    {
        options ??= DefaultOptions;

        return new ValueConverter<T, string>(
            v => JsonSerializer.Serialize(v, options),
            v => JsonSerializer.Deserialize<T>(v, options)!);
    }

    public static readonly JsonSerializerOptions DefaultOptions =
        new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
}
