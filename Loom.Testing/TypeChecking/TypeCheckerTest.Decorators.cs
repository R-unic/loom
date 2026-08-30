using Loom.Core.Diagnostics;
using Loom.Testing;

namespace Loom.Testing.TypeChecking;

public partial class TypeCheckerTest
{
    [Fact]
    public void Checks_BareDecoratorAttribute_NoErrors() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                fn log(f: fn(): void, name: string): void { f(); }
                [log]
                fn do_something() { }
                """
            )
        );

    [Fact]
    public void Checks_GenericDecoratorFactory_NoErrors() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                fn log(ctx: string) -> fn<T>(f: fn(): T, name: string): T {
                    print(ctx);
                    return f();
                };

                [log("info")]
                fn do_something -> print("did something!");
                """
            )
        );

    [Fact]
    public void Checks_Decorator_OnFunctionWithParameters_NoErrors() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                fn log(f: fn(): number, name: string): number { return f(); }

                [log]
                fn add(a: number, b: number): number {
                    return a + b;
                }
                """
            )
        );

    [Fact]
    public void Checks_ChainedDecorators_NoErrors() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                fn a(f: fn(): void, name: string): void { f(); }
                fn b(f: fn(): void, name: string): void { f(); }

                [a, b]
                fn do_something() { }
                """
            )
        );

    [Fact]
    public void ThrowsFor_Decorator_ReturnTypeMismatch()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            fn bad_decorator(f: fn(): number, name: string): string {
                return "oops";
            }

            [bad_decorator]
            fn compute(): number {
                return 42;
            }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.InvalidDecorator,
            "Decorator must return a value assignable to 'number', but returns 'string'."
        );
    }

    [Fact]
    public void ThrowsFor_Decorator_WrongArity()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            fn one_arg(f: fn(): number): number {
                return f();
            }

            [one_arg]
            fn compute(): number {
                return 42;
            }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.InvalidDecorator,
            "Decorators must accept the decorated value and its name as arguments."
        );
    }

    [Fact]
    public void ThrowsFor_Decorator_NonFunctionAttribute()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            let not_a_function = 1;
            [not_a_function]
            fn do_something() { }
            """
        );

        Utility.AssertDiagnostic(diagnostics, InternalCodes.NonFunctionAttribute, "Only functions may be used as attributes.");
    }

    [Fact]
    public void Checks_InterfaceDecorator_NoErrors() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                fn validate(): void { }
                [validate]
                interface Foo { x: number }
                let foo = new Foo { x: 1 };
                """
            )
        );

    [Fact]
    public void ThrowsFor_InterfaceDecorator_ReturnTypeMismatch()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            fn bad_validate(): number { return 1; }
            [bad_validate]
            interface Foo { x: number }
            """
        );

        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidDecorator, "Decorator must return 'void', but returns 'number'.");
    }

    [Fact]
    public void Checks_TopLevelEventDecorator_NoErrors() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                fn log_event(): void { }
                [log_event]
                event scored(points: number);
                """
            )
        );

    [Fact]
    public void ThrowsFor_TopLevelEventDecorator_ReturnTypeMismatch()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            fn bad_log(): number { return 1; }
            [bad_log]
            event scored(points: number);
            """
        );

        Assert.Contains(diagnostics.Set, d => d.Code == InternalCodes.InvalidDecorator);
    }

    [Fact]
    public void ThrowsFor_InterfaceMemberEventDecorator_ReturnTypeMismatch()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            fn bad_log(): number { return 1; }
            interface Foo {
                [bad_log]
                event bar(x: number);
            }
            """
        );

        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidDecorator, "Decorator must return 'void', but returns 'number'.");
    }

    [Fact]
    public void Checks_PropertyDecorator_NoErrors() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                fn clamp(): void { }
                interface Account {
                    [clamp]
                    balance: number
                }
                let a = new Account { balance: 10 };
                """
            )
        );

    [Fact]
    public void ThrowsFor_PropertyDecorator_ReturnTypeMismatch()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            fn bad_clamp(): string { return "oops"; }
            interface Account {
                [bad_clamp]
                balance: number
            }
            """
        );

        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidDecorator, "Decorator must return 'void', but returns 'string'.");
    }

    [Fact]
    public void ThrowsFor_PassiveDecorator_NonConstantArgument()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            fn tag(id: number): void { }
            let x = 5;
            interface Account {
                [tag(x)]
                balance: number
            }
            """
        );

        Utility.AssertDiagnostic(diagnostics, InternalCodes.DecoratorArgumentNotConstant, "Decorator arguments must be compile-time constants.");
    }

    [Fact]
    public void Checks_PassiveDecorator_ConstantEnumArgument_NoErrors() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                enum Level { Low = 1, High = 2 }
                fn tag(level: Level): void { }
                interface Account {
                    [tag(Level::High)]
                    balance: number
                }
                """
            )
        );

    [Fact]
    public void ThrowsFor_PassiveDecorator_FactoryStyleNotAllowed()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            fn log(ctx: string) -> fn<T>(f: fn(): T, name: string): T {
                print(ctx);
                return f();
            };
            interface Account {
                [log("info")]
                balance: number
            }
            """
        );

        Assert.Contains(diagnostics.Set, d => d.Code == InternalCodes.InvalidDecorator);
    }

    [Fact]
    public void Checks_AttributeUsage_AllowsMatchingTarget() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                [attribute_usage(AttributeTargets::Property)]
                fn tag(): void { }
                interface Account {
                    [tag]
                    balance: number
                }
                """
            )
        );

    [Fact]
    public void ThrowsFor_AttributeUsage_DisallowedTarget()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            [attribute_usage(AttributeTargets::Function)]
            fn tag(): void { }
            interface Account {
                [tag]
                balance: number
            }
            """
        );

        Utility.AssertDiagnostic(diagnostics, InternalCodes.AttributeTargetNotAllowed, "Attribute 'tag' is not valid on Property.");
    }

    [Fact]
    public void Checks_AttributeUsage_CombinedFlags_NoErrors() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                [attribute_usage(AttributeTargets::Property | AttributeTargets::Event)]
                fn tag(): void { }
                interface Account {
                    [tag]
                    balance: number,
                    [tag]
                    event changed(value: number);
                }
                """
            )
        );

    [Fact]
    public void ThrowsFor_AttributeUsage_OnNonFunctionDeclaration()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            [attribute_usage(AttributeTargets::Property)]
            interface Account {
                balance: number
            }
            """
        );

        Utility.AssertDiagnostic(diagnostics, InternalCodes.AttributeUsageNotOnFunction, "'attribute_usage' may only be applied to a function declaration.");
    }

    [Fact]
    public void Checks_AttributeUsage_OnFunctionDecorator_NoErrors() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                [attribute_usage(AttributeTargets::Function)]
                fn log(f: fn(): void, name: string): void { f(); }
                [log]
                fn do_something() { }
                """
            )
        );

    [Fact]
    public void ThrowsFor_AttributeUsage_DisallowedOnFunctionDecorator()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            [attribute_usage(AttributeTargets::Property)]
            fn log(f: fn(): void, name: string): void { f(); }
            [log]
            fn do_something() { }
            """
        );

        Utility.AssertDiagnostic(diagnostics, InternalCodes.AttributeTargetNotAllowed, "Attribute 'log' is not valid on Function.");
    }

    [Fact]
    public void ThrowsFor_AttributeUsage_DisallowedOnFunctionDecorator_ForIntrinsicDecorator()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            [serializable]
            fn foo(): void {}
            """
        );

        Utility.AssertDiagnostic(diagnostics, InternalCodes.AttributeTargetNotAllowed, "Attribute 'serializable' is not valid on Function.");
    }

    [Fact]
    public void ThrowsFor_AttributeUsage_DisallowedOnInterfaceDecorator_ForIntrinsicDecorator()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            [metadata_only]
            interface Foo {}
            """
        );

        Utility.AssertDiagnostic(diagnostics, InternalCodes.AttributeTargetNotAllowed, "Attribute 'metadata_only' is not valid on Interface.");
    }

    [Fact]
    public void Checks_IntrinsicAttribute_OnEvent_NotTreatedAsDecorator() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                [luau_name("NotUsed")]
                event my_event(param: string);
                """
            )
        );

    [Fact]
    public void Checks_MetadataOnlyDecorator_OnFunction_NoErrors() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                [metadata_only]
                fn replicated(): void {}

                [replicated]
                fn greet(name: string) {
                    print(name);
                }
                """
            )
        );

    [Fact]
    public void Checks_MetadataOnlyDecoratorFactory_OnFunction_NoErrors() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                [metadata_only]
                fn tag(name: string): void {}

                [tag("admin")]
                fn greet(name: string) {
                    print(name);
                }
                """
            )
        );

    [Fact]
    public void ThrowsFor_MetadataOnlyDecorator_NonVoidReturn()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            [metadata_only]
            fn tag(x: number): number { return x; }

            [tag(1)]
            fn greet(name: string) {
                print(name);
            }
            """
        );

        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidDecorator, "Decorator must return 'void', but returns 'number'.");
    }

    [Fact]
    public void ThrowsFor_MetadataOnlyDecorator_NonConstantArgument()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            [metadata_only]
            fn tag(x: string): void {}

            let value = "hi";
            [tag(value)]
            fn greet(name: string) {
                print(name);
            }
            """
        );

        Utility.AssertDiagnostic(diagnostics, InternalCodes.DecoratorArgumentNotConstant, "Decorator arguments must be compile-time constants.");
    }

    [Fact]
    public void Checks_NonMetadataOnlyDecorator_StillRequiresWrapShape()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            fn tag(): void {}

            [tag]
            fn greet(name: string) {
                print(name);
            }
            """
        );

        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidDecorator, "Decorators must accept the decorated value and its name as arguments.");
    }
}
