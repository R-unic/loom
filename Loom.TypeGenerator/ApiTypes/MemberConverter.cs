using System.Text.Json;
using System.Text.Json.Serialization;

namespace Loom.TypeGenerator.ApiTypes;

internal sealed class MemberConverter : JsonConverter<MemberBase>
{
    public override MemberBase Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var memberType = root.GetProperty("MemberType").GetString();
        MemberBase? member = memberType switch
        {
            "Callback" => new Callback(),
            "Event" => new Event(),
            "Function" => new Function(),
            "Property" => new Property(),
            _ => null
        };

        if (member == null)
            Log.Fatal($"MemberType '{memberType}' is not supported.");

        foreach (var property in root.EnumerateObject())
            switch (property.Name)
            {
                case "MemberType":
                    member.MemberType = property.Value.GetString()!;
                    break;
                case "Name":
                    member.Name = property.Value.GetString();
                    break;
                case "Security":
                    member.Security = JsonSerializer.Deserialize<object?>(property.Value.GetRawText(), options);
                    break;
                case "Tags":
                    member.Tags = JsonSerializer.Deserialize<List<object>>(property.Value.GetRawText(), options)?.ToHashSet();
                    break;
                case "Description":
                    member.Description = property.Value.GetString()!;
                    break;
                case "Parameters":
                    if (member is Callback callback)
                        callback.Parameters = JsonSerializer.Deserialize<Parameter[]>(property.Value.GetRawText(), options)!;

                    break;
                case "ReturnType":
                    if (member is Callback callback2)
                    {
                        var rawJson = property.Value.GetRawText();
                        var type = rawJson.StartsWith('[')
                            ? JsonSerializer.Deserialize<ValueType[]>(rawJson, options)!
                            : [JsonSerializer.Deserialize<ValueType>(rawJson, options)!];

                        callback2.ReturnType = type;
                    }

                    break;
                case "Category":
                    if (member is Property prop)
                        prop.Category = property.Value.GetString()!;

                    break;
                case "Default":
                    if (member is Property propDefault)
                        propDefault.Default = property.Value.GetString()!;

                    break;
                case "Serialization":
                    if (member is Property propSerialization)
                        propSerialization.Serialization = JsonSerializer.Deserialize<Serialization>(property.Value.GetRawText(), options)!;

                    break;
                case "ThreadSafety":
                    if (member is Property propThreadSafety)
                        propThreadSafety.ThreadSafety = property.Value.GetString()!;

                    break;
                case "ValueType":
                    if (member is Property propValueType)
                        propValueType.ValueType = JsonSerializer.Deserialize<ValueType>(property.Value.GetRawText(), options)!;

                    break;
            }

        return member;
    }

    public override void Write(Utf8JsonWriter writer, MemberBase value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
}