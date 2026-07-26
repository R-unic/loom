using System.Text;

namespace Loom.TypeGenerator.Generators;

internal abstract class BaseGenerator(string filePath, ReflectionMetadataReader metadata)
{
    private readonly string _typeGeneratorDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../.."));
    protected readonly string FilePath = filePath;

    protected readonly ReflectionMetadataReader Metadata = metadata;
    public readonly StringBuilder Stream = new(4096);

    private int _indentLevel;

    protected void WriteContentsOfFile(string fileName)
    {
        var filePath = $"{_typeGeneratorDirectory}/{fileName}";
        Stream.AppendLine(File.ReadAllText(filePath));
        Write();
        Log.Info($"wrote contents of '{filePath}'");
    }

    protected void WriteBlock(string header, Action writeBlock, bool finalNewline = true)
    {
        Write(header + " {");
        PushIndent();
        writeBlock();
        PopIndent();
        Write("}");

        if (!finalNewline) return;
        Write();
    }

    protected void WriteList(IEnumerable<string> enumerable) => WriteList(enumerable, s => s);

    protected void WriteList<T>(IEnumerable<T> enumerable, Func<T, string> renderElement)
    {
        foreach (var item in enumerable)
            Write(renderElement(item));
    }

    protected void Write() => Write("");

    protected void Write(string line) => Stream.Append('\t', _indentLevel).Append(line).Append('\n');

    protected void PushIndent() => _indentLevel++;
    protected void PopIndent() => _indentLevel--;
}