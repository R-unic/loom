#pragma warning disable CS8618

// ReSharper disable UnusedAutoPropertyAccessor.Global
using System.Text.Json.Serialization;

namespace Loom.TypeGenerator.ApiTypes;

[JsonConverter(typeof(MemberConverter))]
internal abstract class MemberBase
{
    public string MemberType { get; set; }
    public string? Name { get; set; }
    public object? Security { get; set; } // string or Security
    public HashSet<object>? Tags { get; set; }
    public string Description { get; set; }
}

internal sealed class Parameter
{
    public string Name { get; set; }
    public ValueType Type { get; set; }
    public string Default { get; set; }
}

internal class Callback : MemberBase
{
    public Parameter[] Parameters { get; set; }
    [JsonConverter(typeof(SingleOrArrayConverter<ValueType>))]
    public ValueType[]? ReturnType { get; set; }
}

internal sealed class Event : Callback;
internal sealed class Function : Callback;

internal sealed class Property : MemberBase
{
    public string Category { get; set; }
    public string Default { get; set; }
    public Serialization Serialization { get; set; }
    public string ThreadSafety { get; set; }
    public ValueType ValueType { get; set; }
}

internal sealed class Class
{
    public MemberBase[] Members { get; set; }
    public string MemberCategory { get; set; }
    public HashSet<object>? Tags { get; set; }
    public string ThreadSafety { get; set; }
    public string Name { get; set; }
    public string Superclass { get; set; }

    // public HashSet<string> Subclasses { get; set; }
    public string? Description { get; set; }
}