using Enum = Loom.TypeGenerator.ApiTypes.Enum;

namespace Loom.TypeGenerator.Generators;

internal class EnumGenerator(string filePath, ReflectionMetadataReader metadata)
    : BaseGenerator(filePath, metadata)
{
    // TODO: Enum methods, Enum.Member methods, EnumItem methods

    public void Generate(Enum[] enums)
    {
        Write("## Generated Roblox enums");
        Write();
        WriteBlock(
            "declare interface EnumItem<Kind: string>",
            () =>
            {
                Write("_kind: Kind;");
                Write("Name: string;");
                Write("Value: number;");
                Write("EnumType: never;");
            }
        );

        var enumNames = new List<string>();
        for (var i = 0; i < enums.Length; i++)
        {
            var rbxEnum = enums[i];
            enumNames.Add(rbxEnum.Name);
            GenerateEnum(rbxEnum, i == enums.Length - 1);
        }

        WriteBlock("declare interface EnumStatic", () => WriteList(enumNames, name => $"{name}: Enum{name};"));
        WriteBlock("declare interface Enum", () => WriteList(enumNames, name => $"{name}: EnumItem<\"{name}\">;"));
        Write("declare let Enum: EnumStatic;");
    }

    private void GenerateEnum(Enum rbxEnum, bool isLast) =>
        WriteBlock(
            $"declare interface Enum{rbxEnum.Name}",
            () => WriteList(rbxEnum.Items, item => $"{item.Name}: EnumItem<\"{rbxEnum.Name}\">;"),
            !isLast
        );
}