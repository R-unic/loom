#pragma warning disable CS8618
namespace Loom.TypeGenerator.ApiTypes;

internal sealed class Dump
{
    public Class[] Classes { get; init; }
    public Enum[] Enums { get; init; }
    public float Version { get; init; }
}