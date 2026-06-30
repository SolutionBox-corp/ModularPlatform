using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModularPlatform.Crm.Json;

/// <summary>
/// PATCH presence wrapper: distinguishes "property absent from the JSON body" (<see cref="IsSpecified"/> = false) from
/// "property present, possibly null" (true). System.Text.Json only invokes the converter for properties that ARE in the
/// payload, so an omitted property leaves the struct at its default (unspecified) — exactly the distinction a partial
/// PATCH needs to tell "leave unchanged" apart from "set to null" for a nullable field that has no empty-string sentinel
/// (e.g. a <c>Guid?</c> foreign key the caller wants to detach).
/// </summary>
[JsonConverter(typeof(OptionalJsonConverterFactory))]
public readonly struct Optional<T>
{
    public Optional(T? value)
    {
        Value = value;
        IsSpecified = true;
    }

    /// <summary>True when the property was present in the payload (even if its value was null).</summary>
    public bool IsSpecified { get; }

    public T? Value { get; }
}

internal sealed class OptionalJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Optional<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var valueType = typeToConvert.GetGenericArguments()[0];
        return (JsonConverter)Activator.CreateInstance(
            typeof(OptionalJsonConverter<>).MakeGenericType(valueType))!;
    }
}

internal sealed class OptionalJsonConverter<T> : JsonConverter<Optional<T>>
{
    public override Optional<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(JsonSerializer.Deserialize<T>(ref reader, options));

    public override void Write(Utf8JsonWriter writer, Optional<T> value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value.Value, options);
}
