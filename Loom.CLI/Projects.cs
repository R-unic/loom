using System.Diagnostics.CodeAnalysis;
using Loom.Config;
using Loom.Core.Pipeline;

namespace Loom.CLI;

/// <summary>
///     Finding the project a command was pointed at, and reporting what stopped it before a file was read. Every
///     verb needs both, and a project that cannot be read should read the same whichever verb found that out.
/// </summary>
internal static class Projects
{
    /// <summary>
    ///     The manifest of the project in <paramref name="directory" />, or <see langword="false" /> having said why
    ///     there is none to read.
    /// </summary>
    public static bool TryLocate(string directory, [NotNullWhen(true)] out LoomConfig? config)
    {
        config = ConfigReader.LocateFromDirectory(directory, out var diagnostics);
        if (config != null)
            return true;

        if (diagnostics.Count == 0)
            Log.Fatal($"could not locate Loom configuration file in directory '{directory}'.");
        else
            Console.WriteLine(string.Join(Environment.NewLine, diagnostics.Select(diagnostic => $"({ConfigReader.ConfigFileName}) {diagnostic}")));

        return false;
    }

    /// <summary>
    ///     Reports what a package manager or a project load could not do: a lock file that cannot be trusted, a
    ///     dependency that is not installed, a package that is not published. Each names the file or the package it
    ///     is about, so none is prefixed with one.
    /// </summary>
    public static void Report(IEnumerable<ConfigDiagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
            Log.Fatal(diagnostic.ToString());
    }
}
