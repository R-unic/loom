using Loom.Core.Diagnostics;

namespace Loom.Testing;

/// <summary>
///     A type alias whose body names the very interfaces that reference it back. Resolving the first
///     interface pulls in the alias, which pulls in the second, whose members name the alias again -
///     and that innermost reference used to find an unbound type variable that settled as 'never',
///     so the second interface's member came out with a different type from the first's. A member is
///     only callable on a union when every arm agrees, so the mismatch made it uncallable.
/// </summary>
[Collection("Assembly")]
public class RecursiveTypeAliasTest
{
    private const string MutuallyRecursive = """
        interface A<T, E> {
            tag: true;
            go: fn<U>(f: fn(v: T): R<U, E>): R<U, E>;
        }

        interface B<T, E> {
            tag: false;
            go: fn<U>(f: fn(v: T): R<U, E>): R<U, E>;
        }

        type R<T, E> = A<T, E> | B<T, E>;

        declare let a: A<number, string>;
        declare let b: B<number, string>;


        """;

    [Fact]
    public void BothArmsResolveTheAliasIdentically()
    {
        var first = Utility.GetLastStatementType(MutuallyRecursive + "a.go").ToString();
        var second = Utility.GetLastStatementType(MutuallyRecursive + "b.go").ToString();

        Assert.DoesNotContain("never", second);
        Assert.Equal(first, second);
    }

    [Fact]
    public void TheAliasStillResolvesInsideEachArmsOwnMembers()
    {
        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(MutuallyRecursive + "a.tag"));
        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(MutuallyRecursive + "b.tag"));
    }

    [Fact]
    public void AMemberOfTheAliasedUnionIsCallable()
    {
        const string source = """
            interface A<T, E> {
                tag: true;
                go: fn(f: fn(v: T): R<T, E>): R<T, E>;
            }

            interface B<T, E> {
                tag: false;
                go: fn(f: fn(v: T): R<T, E>): R<T, E>;
            }

            type R<T, E> = A<T, E> | B<T, E>;

            declare let r: R<number, string>;
            declare fn step(v: number): R<number, string>;

            let chained = r.go(step);
            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));
    }

    /// <summary>
    ///     'Result' is exactly this shape - an alias over two interfaces whose own members name it
    ///     back - so 'and_then' can be declared with the alias rather than the union spelled out.
    /// </summary>
    [Fact]
    public void AndThenChainsThroughTheAlias()
    {
        const string prefix = """
            fn step(n: number): Result<number, string> {
                return Result.ok(n + 1);
            }

            fn start(): Result<number, string> {
                return Result.ok(1);
            }


            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(prefix + "let c: Result<number, string> = start().and_then(step);"));

        Utility.AssertDiagnostic(
            Utility.GetTypeCheckerDiagnostics(prefix + "let c: Result<string, string> = start().and_then(step);"),
            InternalCodes.TypeMismatch,
            "Type 'fn(number): Result<number, string>' is not assignable to type 'fn(number): Result<string, string>'.\n"
            + "    Type 'ResultOk | ResultError' is not assignable to type 'ResultOk | ResultError'."
        );
    }
}
