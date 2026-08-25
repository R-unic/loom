using Loom.Core.Diagnostics;

namespace Loom.Testing;

[Collection("Assembly")]
public class FlowAnalyzerTest
{
    [Fact]
    public void Allows_IntrinsicResultReference()
    {
        var diagnostics = Utility.FlowAnalyze("some_fn(BaseResult::ok);").AnalyzerResult.Diagnostics;
        Utility.AssertNoErrors(diagnostics);
    }

    [Theory]
    [InlineData("fn foo { return 42; let x = 1 }")]
    [InlineData("while true { break; let unreachable = 1; }")]
    [InlineData("while true { continue; let unreachable = 1; }")]
    [InlineData("fn test() { after 1s { return 42; let x = 1; } }")]
    public void WarnsFor_UnreachableCode(string source)
    {
        var diagnostics = Utility.FlowAnalyze(source).AnalyzerResult.Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.UnreachableCode, "Unreachable code detected.");
    }

    [Theory]
    [InlineData("let x = x;")]
    [InlineData("mut x; x;")]
    [InlineData("mut x: number; x += 1;")]
    [InlineData("mut x: number; { let x = 42; }; x;")]
    [InlineData("mut x: number; { x; }")]
    [InlineData("mut x: number; let arr = [0]; arr[0] = 42; x;")]
    [InlineData("""mut x: number; $"{x}";""")]
    [InlineData("mut x: number; let f = fn(): number { return x; };")]
    public void ThrowsFor_UseOfUninitialized(string source)
    {
        var diagnostics = Utility.FlowAnalyze(source).AnalyzerResult.Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.UseOfUninitialized, "Use of uninitialized variable 'x'.");
    }

    [Theory]
    [InlineData("mut x: number; if true { x = 69; } x += 1")]
    [InlineData("mut x: number; if true x = 69; x += 1")]
    [InlineData("mut x: number; for _ : 1..10 { x = 42; } x;")]
    [InlineData("mut x: number; after 1s { x = 42; } x;")]
    [InlineData("mut x: number; while true { x = 1; } x;")]
    [InlineData("mut x: number; while true { x = 1; break; } x;")]
    [InlineData("mut x: number; while true { x = 1; break; } x += 69;")]
    [InlineData("mut x: number; if true x = 69; x;")]
    [InlineData("mut x: number; if true x = 69 else if true x = 420; x;")]
    [InlineData(
        """
        mut x: number;
        if true {
            if true {
                x = 42;
            }
        } else {
            x = 0;
        }
        x;
        """
    )]
    [InlineData(
        """
        mut x: number;
        while true {
            if true {
                x = 1;
                break;
            }
        }
        x;
        """
    )]
    [InlineData(
        """
        mut x: number;
        while true {
            while true {
                x = 1;
                break;
            }

            break;
        }
        x;
        """
    )]
    public void ThrowsFor_UseOfMaybeUninitialized(string source)
    {
        var diagnostics = Utility.FlowAnalyze(source).AnalyzerResult.Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.UseOfMaybeUninitialized, "Variable 'x' might not be initialized on this path.");
    }

    [Theory]
    [InlineData("let x = 1; x = 69")]
    [InlineData("let x = 1; x += 69")]
    [InlineData("let x = 1; { x = 69 }")]
    [InlineData("let x = 1; { x += 69 }")]
    [InlineData("for x : 1..10 { x = 69 }")]
    [InlineData("for x : 1..10 { x += 69 }")]
    [InlineData("declare let x: number; x = 42")]
    [InlineData("declare let x: number; x += 69")]
    [InlineData("declare let x: number; { x = 69 }")]
    [InlineData("declare let x: number; { x += 69 }")]
    public void ThrowsFor_AssignToImmutable(string source)
    {
        var diagnostics = Utility.FlowAnalyze(source).AnalyzerResult.Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.AssignToImmutable, "Cannot assign to immutable variable 'x'.");
    }

    [Theory]
    [InlineData("mut x: number; x = 1; let f = fn(): number { return x; }; f();")]
    [InlineData("fn abc(x: number) -> print(x)")]
    [InlineData("fn abc(x: number) -> abc(x)")]
    [InlineData(
        """
        mut x: number;
        if outer {
            if inner {
                return;
            }
            x = 1;
        } else {
            x = 2;
        }

        x;
        """
    )]
    [InlineData(
        """
        mut x: number;
        if cond
            x = 1;
        else
            x = 2;
        x;
        """
    )]
    [InlineData(
        """
        mut x: number;
        if cond {
            x = 1;
        } else if other {
            x = 2;
        } else {
            x = 3;
        }
        x;
        """
    )]
    [InlineData("enum Colors { Red, Green, Blue }; Colors::Red;")]
    [InlineData("mut x: number; if x = 69 { x += 1; }")]
    [InlineData("mut x: number; if x = 69 { }; x += 1;")]
    [InlineData(
        """
        fn test() {
            mut x: number;
            if true {
                return;
            } else {
                x = 1;
            }
            x;
        }
        """
    )]
    public void Allows(string source) => Utility.AssertNoErrors(Utility.FlowAnalyze(source).AnalyzerResult);

    [Theory]
    [InlineData("match 1 { x -> x }")]
    [InlineData("match 1 { let name -> name }")]
    [InlineData("match 1 { n when n > 0 -> n }")]
    [InlineData("match 1 { [a, b, c] -> a }")]
    [InlineData("match 1 { [head, ..rest] -> rest }")]
    [InlineData("match 1 { { value } -> value }")]
    [InlineData("match 1 { { ok: true, value: v } -> v }")]
    [InlineData("match 1 { s when string -> s, _ -> \"\" }")]
    [InlineData("mut x: number; match 1 { 0 -> (x = 1), _ -> (x = 2) }; x")]
    public void Allows_Match(string source) => Utility.AssertNoErrors(Utility.FlowAnalyze(source).AnalyzerResult);

    [Theory]
    [InlineData("mut x: number; match 1 { _ -> (x = 1), _ -> 0 }; x")]
    [InlineData("mut x: number; match 1 { 0 -> (x = 1), _ -> 0 }; x")]
    public void ThrowsFor_Match_UseOfMaybeUninitialized(string source)
    {
        var diagnostics = Utility.FlowAnalyze(source).AnalyzerResult.Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.UseOfMaybeUninitialized, "Variable 'x' might not be initialized on this path.");
    }

    [Fact]
    public void ThrowsFor_Match_AssignToImmutablePatternBinding()
    {
        var diagnostics = Utility.FlowAnalyze("match 1 { x -> (x = 2) }").AnalyzerResult.Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.AssignToImmutable, "Cannot assign to immutable variable 'x'.");
    }
}