using System.Diagnostics.CodeAnalysis;
using System.Text;
using Loom.Core;
using Loom.Core.Debug;
using Loom.Core.Lexing;
using Loom.Core.Parsing;

namespace Loom.Tools;

internal static class AstTools
{
    public static void GenerateAstSnapshots(string[] arguments)
    {
        var snapshotsDirectory = arguments.ElementAtOrDefault(1);
        var skipExisting = bool.Parse(arguments.ElementAtOrDefault(2) ?? "true");
        if (!TryValidateDirectory(snapshotsDirectory, out var loomFiles)) return;

        var skipCount = loomFiles
            .Select(loomFile => GenerateFileSnapshots(loomFile, snapshotsDirectory, skipExisting))
            .Count(skipped => skipped);

        Console.WriteLine($"Done! Processed {loomFiles.Length - skipCount} files.");
    }

    public static string GetAstString(string filePath)
    {
        var file = FileManager.LoadSingle(filePath);
        var lexer = new Lexer(file);
        var result = lexer.Tokenize();
        var parser = new Parser(result);
        var tree = parser.Parse().Tree;
        return AstInspector.Inspect(tree);
    }

    private static bool GenerateFileSnapshots(string loomFile, string snapshotsDirectory, bool skipUnchanged)
    {
        var astString = GetAstString(loomFile);
        var baseName = Path.GetFileNameWithoutExtension(loomFile);
        var outputFilePath = Path.Combine(snapshotsDirectory, $"{baseName}.ast");
        if (skipUnchanged && File.Exists(outputFilePath) && File.ReadAllText(outputFilePath) == astString)
        {
            Console.WriteLine($"Skipping {Path.GetFileName(loomFile)}.");
            return true;
        }

        Console.WriteLine($"Processing: {Path.GetFileName(loomFile)} -> {baseName}.ast");
        try
        {
            File.WriteAllText(outputFilePath, astString, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Error processing {loomFile}: {ex.Message}");
        }

        return false;
    }

    private static bool TryValidateDirectory([NotNullWhen(true)] string? snapshotsDirectory, [MaybeNullWhen(false)] out string[] loomFiles)
    {
        if (string.IsNullOrEmpty(snapshotsDirectory))
        {
            Console.WriteLine("Usage: loomtools generate-ast-snapshots <snapshots-directory>");
            loomFiles = null;
            return false;
        }

        if (!Directory.Exists(snapshotsDirectory))
        {
            Console.WriteLine($"Directory not found: {snapshotsDirectory}");
            loomFiles = null;
            return false;
        }

        loomFiles = Directory.GetFiles(snapshotsDirectory, "*.loom");
        if (loomFiles.Length != 0)
            return true;

        Console.WriteLine($"No .loom files found in {snapshotsDirectory}");
        return false;
    }
}