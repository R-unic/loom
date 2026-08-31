using Loom.Core.Diagnostics;

namespace Loom.Testing.TypeChecking;

public partial class TypeCheckerTest
{
    [Fact]
    public void Unifies_FunctionTypes_WithSelfReferentialParameterType_WithoutStackOverflow()
    {
        const string source = """
            declare interface Node {
                next: Node;
            }

            declare fn a(n: Node): void;
            let b: fn(n: Node): void = a;
            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));
    }

    [Fact]
    public void ThrowsFor_FunctionTypes_WithSelfReferentialParameterType_AndMismatchedReturnType()
    {
        const string source = """
            declare interface Node {
                next: Node;
            }

            declare fn a(n: Node): void;
            let b: fn(n: Node): number = a;
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Assert.Contains(diagnostics.Set, d => d.Code == InternalCodes.TypeMismatch);
    }

    /// <summary>
    ///     Two generic interfaces whose members name the interface itself, compared structurally, with the
    ///     type argument arriving from inference rather than from source. Each of those on its own was fine;
    ///     together they expanded forever and took the process down with them
    ///     (<see href="https://github.com/rbx-loom/loom/issues/194" />).
    /// </summary>
    [Fact]
    public void Checks_SelfReferentialGenerics_ComparedWithAnInferredTypeArgument_NoErrors()
    {
        const string source = """
            declare interface Bag<T> {
                [T]: bool;
                merge: fn(other: Bag<T>): Bag<T>;
            }

            declare interface MutBag<T> {
                mut [T]: bool;
                merge: fn(other: Bag<T>): Bag<T>;
                add: fn(value: T): void;
            }

            declare fn make<T>(..values: T[]): MutBag<T>;
            declare fn take(b: Bag<number>): void;

            fn main(): void {
                let m = make(1, 2);
                take(m);
            }
            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));
    }

    /// <summary>Written out rather than inferred, the same pair has to stay clean.</summary>
    [Fact]
    public void Checks_SelfReferentialGenerics_ComparedWithAnExplicitTypeArgument_NoErrors()
    {
        const string source = """
            declare interface Bag<T> {
                [T]: bool;
                merge: fn(other: Bag<T>): Bag<T>;
            }

            declare interface MutBag<T> {
                [T]: bool;
                merge: fn(other: Bag<T>): Bag<T>;
                add: fn(value: T): void;
            }

            declare let m: MutBag<number>;
            declare fn take(b: Bag<number>): void;

            fn main(): void {
                take(m);
            }
            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));
    }
}
