using Loom.TypeGenerator.ApiTypes;

namespace Loom.TypeGenerator.Generators;

internal class Generator(
    string filePath,
    ReflectionMetadataReader metadata,
    HashSet<string> definedClassNames,
    string security,
    string? lowerSecurity = null
)
    : BaseGenerator(filePath, metadata)
{
    public void Generate(Dump dump)
    {
        try
        {
            Write("## This is an automatically generated file and it should not be edited manually.");

            var enumGenerator = new EnumGenerator(FilePath, Metadata);
            enumGenerator.Generate(dump.Enums);

            var classGenerator = new ClassGenerator(FilePath, Metadata, definedClassNames, security, lowerSecurity);
            classGenerator.Generate(dump.Classes);

            WriteContentsOfFile("roblox.loom");
            Stream.Append(enumGenerator.Stream);
            Stream.Append(classGenerator.Stream);
            WriteFile();
        }
        catch (Exception e)
        {
            Log.Fatal($"unexpected error: {e.Message}");
        }
    }

    private void WriteFile()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, Stream.ToString());
    }
}