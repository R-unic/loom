using System.Diagnostics.CodeAnalysis;

namespace Loom.Core.Text;

public sealed class SourceFile
{
    public static readonly SourceFile Empty = new("<anonymous>", string.Empty);

    private int[]? _lineStarts;

    public SourceFile(string absolutePath, string? sourceText = null)
    {
        AbsolutePath = absolutePath;
        Name = Path.GetFileName(absolutePath);
        SourceText = sourceText ?? File.ReadAllText(absolutePath);
        IsDeclaration = Name.EndsWith(".d.loom");
    }

    public string AbsolutePath { get; }
    public string Name { get; }
    public string SourceText { get; }
    public bool IsDeclaration { get; set; }
    public bool IsIntrinsic { get; internal set; }

    /// <summary>
    ///     The file's <c>###</c> doc comments, filled in by the lexer. Empty until the file has been
    ///     tokenized, which is why nothing raises when a caller asks a file the compiler never read.
    /// </summary>
    public DocumentationTable Documentation { get; internal set; } = DocumentationTable.Empty;

    public override string ToString() => Name;
    public string RelativePath(string to = ".") => Path.GetRelativePath(to, AbsolutePath);

    public int GetSourcePosition(int character, int line)
    {
        BuildLineStarts();
        return _lineStarts[line] + character;
    }

    public int GetLineFromPosition(int position)
    {
        BuildLineStarts();
        var line = Array.BinarySearch(_lineStarts, position);
        return line >= 0 ? 1 + line : ~line;
    }

    public int GetCharacterFromPosition(int position)
    {
        BuildLineStarts();
        var line = GetLineFromPosition(position);
        return position - _lineStarts[line - 1];
    }

    [MemberNotNull(nameof(_lineStarts))]
    private void BuildLineStarts()
    {
        if (_lineStarts is not null) return;

        var list = new List<int> { 0 };
        for (var i = 0; i < SourceText.Length; i++)
            if (SourceText[i] == '\n')
                list.Add(i + 1);

        _lineStarts = list.ToArray();
    }
}