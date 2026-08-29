using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving.Symbols;

namespace Loom.Testing;

public partial class ResolverTest
{
    [Fact]
    public void Resolves_DecoratorAttribute_AsOrdinaryValueReference() =>
        Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                fn log(f: fn(): void, name: string): void { f(); }
                [log]
                fn do_something() { }
                """
            )
        );

    [Fact]
    public void ThrowsFor_DecoratorAttribute_UnknownName()
    {
        var diagnostics = Utility.GetSemanticModel("[unknown_decorator]\nfn do_something() { }").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindName, "Cannot find name 'unknown_decorator'.");
    }

    [Fact]
    public void Resolves_InterfaceDecoratorAttribute_AsOrdinaryValueReference() =>
        Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                fn validate(f: fn(): Foo, name: string): Foo { return f(); }
                [validate]
                interface Foo { x: number }
                """
            )
        );

    [Fact]
    public void ThrowsFor_InterfaceDecoratorAttribute_UnknownName()
    {
        var diagnostics = Utility.GetSemanticModel("[unknown_decorator]\ninterface Foo { x: number }").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindName, "Cannot find name 'unknown_decorator'.");
    }
}
