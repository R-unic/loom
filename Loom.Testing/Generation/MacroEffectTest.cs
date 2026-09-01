using NuLua;
using NuLua.Luau;

namespace Loom.Testing.Generation;

/// <summary>
///     Executes the macros that read one operand more than once, and counts how often the operand
///     actually ran.
/// </summary>
/// <remarks>
///     A macro expands one Loom expression into several Luau ones, and an operand written once in the
///     source has to run once no matter how many places the expansion needs it. Reading a snapshot
///     cannot tell you it ran twice - both spellings look reasonable - so these count the calls.
/// </remarks>
[Collection("Assembly")]
public class MacroEffectTest
{
    private const string Counter = """
        mut calls = 0;

        fn index(): number {
            calls += 1;
            return 2;
        }

        fn text(): string {
            calls += 1;
            return "hello";
        }


        """;

    [Theory]
    // 'string.sub' takes the index twice, so an effectful index would run once per argument.
    [InlineData("let value = \"hello\"[index()];", "1")]
    // A range slice measures the target and then slices it.
    [InlineData("let value = text()[1..3];", "1")]
    [InlineData("let value = text().starts_with(\"he\");", "1")]
    [InlineData("let value = text().ends_with(\"lo\");", "1")]
    public void RunsTheOperandOnce(string source, string expected) => Assert.Equal(expected, Run($"{Counter}{source}", "calls"));

    [Theory]
    [InlineData("\"hello\"[2]", "e")]
    [InlineData("\"hello\"[1..3]", "hel")]
    [InlineData("\"hello\".starts_with(\"he\")", "true")]
    [InlineData("\"hello\".starts_with(\"lo\")", "false")]
    // A one-character suffix folds the start position down to '#s' with no arithmetic left.
    [InlineData("\"hello\".ends_with(\"o\")", "true")]
    [InlineData("\"hello\".ends_with(\"lo\")", "true")]
    [InlineData("\"hello\".ends_with(\"he\")", "false")]
    // A suffix longer than the string must not wrap around and match.
    [InlineData("\"lo\".ends_with(\"hello\")", "false")]
    [InlineData("\"he\".starts_with(\"hello\")", "false")]
    [InlineData("\"hello world\".index_of(\"world\")", "7")]
    [InlineData("\"hello world\".index_of(\"xyz\")", "nil")]
    public void Computes(string expression, string expected) => Assert.Equal(expected, Run($"let value = {expression};", "value"));

    /// <summary>The folded length is a byte count, which is what Luau's '#' would have measured.</summary>
    [Theory]
    [InlineData("\"héllo\".starts_with(\"hé\")", "true")]
    [InlineData("\"héllo\".ends_with(\"llo\")", "true")]
    [InlineData("\"héllo\".starts_with(\"he\")", "false")]
    public void MeasuresLiteralsInBytes(string expression, string expected) => Assert.Equal(expected, Run($"let value = {expression};", "value"));

    private static string Run(string source, string read)
    {
        var luau = Utility.GetLuauAST(source, typeCheck: true).Render().Replace("const ", "local ");
        using var state = LuauState.Create();
        state.OpenLibraries();

        return state.DoString($"{luau}{Environment.NewLine}return tostring({read})")[0].ToString();
    }
}
