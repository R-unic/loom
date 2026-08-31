using Loom.Core.Diagnostics;

namespace Loom.Testing;

internal static partial class Utility
{
    public static T AssertNoErrors<T>(T result)
        where T : DiagnosedResult
    {
        AssertNoErrors(result.Diagnostics);
        return result;
    }

    public static void AssertNoErrors(DiagnosticBag diagnostics) => Assert.DoesNotContain(diagnostics.Set, d => d.Severity == DiagnosticSeverity.Error);

    public static void AssertDiagnostic(DiagnosticBag diagnostics, string code, string message, string? hint = null)
    {
        var diagnostic = diagnostics.Find(d => d.Code == code);
        Assert.NotNull(diagnostic);
        Assert.Equal(message, diagnostic.Message);

        if (hint == null) return;
        Assert.Equal(hint, diagnostic.Hint);
    }

    /// <summary>Asserts that exactly one diagnostic in <paramref name="diagnostics" /> mentions <paramref name="name" />.</summary>
    public static void AssertReportedOnce(DiagnosticBag diagnostics, string name)
    {
        var mentioning = diagnostics.Set.Where(diagnostic => diagnostic.Message.Contains($"'{name}'")).ToList();
        Assert.Single(mentioning);
    }
}
