using Loom.Config;

namespace Loom.Testing;

/// <summary>
///     Runs <c>Conformance/package-name.json</c> against <see cref="PackageName" />, the other half of the corpus
///     <c>rbx-loom/loom-pm</c> shares. Normalisation and squat detection are registry concepts with no counterpart
///     here, so they are tested on the Go side alone and are deliberately absent from the file.
/// </summary>
public class PackageNameConformanceTest
{
    public static IEnumerable<TheoryDataRow<string, bool, string, string>> ParseCases =>
        ConformanceCorpus.Section(ConformanceCorpus.PackageNames, "parse")
            .Select(
                test => new TheoryDataRow<string, bool, string, string>(
                    test.String("text"),
                    test.Bool("valid"),
                    test.TryGetProperty("scope", out var scope) ? scope.GetString()! : string.Empty,
                    test.TryGetProperty("name", out var name) ? name.GetString()! : string.Empty
                )
                {
                    TestDisplayName = test.Describe($"'{test.String("text")}'")
                }
            );

    public static IEnumerable<TheoryDataRow<string, string, int>> CompareCases =>
        ConformanceCorpus.Section(ConformanceCorpus.PackageNames, "compare")
            .Select(
                test => new TheoryDataRow<string, string, int>(
                    test.String("left"),
                    test.String("right"),
                    test.GetProperty("ordering").GetInt32()
                )
                {
                    TestDisplayName = test.Describe($"{test.String("left")} vs {test.String("right")}")
                }
            );

    [Theory]
    [MemberData(nameof(ParseCases))]
    public void Parse(string text, bool valid, string scope, string name)
    {
        Assert.Equal(valid, PackageName.TryParse(text, out var packageName, out _));
        if (!valid)
            return;

        Assert.Equal(scope, packageName!.Scope ?? string.Empty);
        Assert.Equal(name, packageName.Name);
    }

    [Theory]
    [MemberData(nameof(CompareCases))]
    public void Compare(string left, string right, int ordering)
    {
        var (first, second) = (PackageName.Parse(left), PackageName.Parse(right));
        Assert.Equal(ordering, Math.Sign(first.CompareTo(second)));
        Assert.Equal(-ordering, Math.Sign(second.CompareTo(first)));
        Assert.Equal(ordering == 0, first.Equals(second));
    }
}
