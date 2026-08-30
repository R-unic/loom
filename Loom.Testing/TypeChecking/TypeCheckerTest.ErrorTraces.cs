using Loom.Core.Diagnostics;
using Loom.Testing;

namespace Loom.Testing.TypeChecking;

public partial class TypeCheckerTest
{
    [Fact]
    public void ThrowsFor_WholeArrayVariableAssignment_TracesOuterArrayType()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            let a: number[] = [1, 2, 3];
            let b: string[] = a;
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type 'number[]' is not assignable to type 'string[]'.\n    Type 'number' is not assignable to type 'string'."
        );
    }

    [Fact]
    public void ThrowsFor_InterfaceVariableAssignment_TracesInterfaceNamesAndBadProperty()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface A { x: number }
            interface B { x: string }
            declare fn take(value: A): void;
            declare let b: B;
            take(b);
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type 'B' is not assignable to type 'A'.\n    Type '{ x: string }' is not assignable to type '{ x: number }'.\n        Type 'string' is not assignable to type 'number'."
        );
    }

    [Fact]
    public void ThrowsFor_GenericInterfaceVariableAssignment_TracesOuterGenericType()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            declare interface Box<T> { value: T }
            declare let a: Box<number>;
            let b: Box<string> = a;
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type 'Box<number>' is not assignable to type 'Box<string>'.\n    Type '{ value: number }' is not assignable to type '{ value: string }'.\n        Type 'number' is not assignable to type 'string'."
        );
    }
}
