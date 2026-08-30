using Loom.Core.Diagnostics;
using Loom.Core.TypeChecking;
using Loom.Core.TypeChecking.Types;
using Type = Loom.Core.TypeChecking.Types.Type;
using Loom.Testing;

namespace Loom.Testing.TypeChecking;

public partial class TypeCheckerTest
{
    [Fact]
    public void Checks_HoistedInterface_VisitedTwice_KeepsOneTypeInstance()
    {
        const string source = """
            interface EventObject {
                event consumer(param: Thing);
            }

            interface Thing {
                parent: Thing;
                value: string;
            }

            let eo = none as never as EventObject;
            eo.consumer
            """;

        var (_, semanticModel, flowAnalyzer) = Utility.FlowAnalyze(source);
        Utility.AssertNoErrors(new TypeChecker(semanticModel, flowAnalyzer).Check().Diagnostics);

        var declaration = semanticModel.Tree.Statements.OfType<Core.Parsing.AST.InterfaceDeclaration>().Single(statement => statement.Name.Text == "Thing");
        var declaredType = Assert.IsType<InterfaceType>(semanticModel.GetType(declaration));
        var instantiated = Assert.IsType<InstantiatedType>(semanticModel.GetType(semanticModel.Tree.Statements[^1]));
        var eventParameterType = instantiated.Arguments.TakeWhile(Type.IsDefined).Single();
        Assert.Same(declaredType, eventParameterType);
    }

    [Fact]
    public void Checks_EventConnect_NamedHandler_WithSelfReferentialParameterType_NoErrors() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                interface EventObject {
                    event consumer(param: Thing);
                }

                interface Thing {
                    parent: Thing;
                    value: string;
                }

                fn on_consumer(param: Thing): void { }

                let eo = none as never as EventObject;
                eo.consumer += on_consumer
                """
            )
        );

    [Fact]
    public void Checks_InterfaceEventMember_AccessedThroughVariable_TypesAsEvent()
    {
        const string source = """
            interface EventObject {
                event consumer(param: string);
            }

            let eo = none as never as EventObject;
            eo.consumer
            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));
        var type = Utility.GetLastStatementType(source);
        var instantiated = Assert.IsType<InstantiatedType>(type);
        Assert.Equal("Event", instantiated.GenericType.Declaration.Name.Text);
        Assert.True(
            instantiated.Arguments.TakeWhile(Type.IsDefined).Single().Equals(PrimitiveType.String),
            $"Expected first event argument to be 'string', got '{instantiated.Arguments.FirstOrDefault()}'"
        );
    }

    [Fact]
    public void Checks_InterfaceEventMember_Connect_TypesAsEventConnection()
    {
        const string source = """
            interface EventObject {
                event consumer(param: string);
            }

            fn on_consumer(p: string): void { }

            let eo = none as never as EventObject;
            eo.consumer += on_consumer
            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));
        var type = Utility.GetLastStatementType(source);
        var interfaceType = Assert.IsType<InterfaceType>(type);
        Assert.Equal("ScriptConnection", interfaceType.Name);
    }

    [Fact]
    public void Checks_InterfaceEventMember_Once_TypesAsEventConnection()
    {
        const string source = """
            interface EventObject {
                event consumer(param: string);
            }

            fn on_consumer(p: string): void { }

            let eo = none as never as EventObject;
            eo.consumer ^= on_consumer
            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));
        var type = Utility.GetLastStatementType(source);
        var interfaceType = Assert.IsType<InterfaceType>(type);
        Assert.Equal("ScriptConnection", interfaceType.Name);
    }

    [Fact]
    public void Checks_EventOnce_AnonymousHandlerWithUntypedParameter_InfersFromEventDeclaration() =>
        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics("event abc(x: number); abc ^= fn(x) { let y: number = x; };"));

    [Fact]
    public void ThrowsFor_EventOnce_AnonymousHandlerWithUntypedParameter_InferredTypeIsPrecise()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("event abc(x: number); abc ^= fn(x) { let y: string = x; };");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type 'number' is not assignable to type 'string'.");
    }

    [Fact]
    public void Checks_EventConnect_AnonymousHandlerWithUntypedParameter_InfersFromEventDeclaration() =>
        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics("event abc(x: number); abc += fn(x) { let y: number = x; };"));

    [Fact]
    public void ThrowsFor_EventConnect_AnonymousHandlerWithUntypedParameter_InferredTypeIsPrecise()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("event abc(x: number); abc += fn(x) { let y: string = x; };");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type 'number' is not assignable to type 'string'.");
    }

    [Fact]
    public void Checks_EventConnect_AnonymousHandlerWithMixedExplicitAndInferredParameters_NoErrors() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics("event abc(x: number, y: string); abc += fn(x: number, y) { let z: string = y; };")
        );

    [Fact]
    public void ThrowsFor_EventConnect_AnonymousHandlerWithExplicitParameterType_MismatchesEvent()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("event abc(x: number); abc += fn(x: string) { };");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type 'number' is not assignable to type 'string'.");
    }

    /// <remarks>
    ///     Issue #206. A rest parameter lives on the declaration, not on <c>Event&lt;T1..T8&gt;</c>, which is
    ///     positional - so the array it declares used to arrive as one ordinary parameter and a handler
    ///     naming the arguments individually was rejected.
    /// </remarks>
    /// <remarks>
    ///     A handler is free to ignore arguments it is handed, which is how most Roblox events are used.
    ///     Assignability always allowed it and unification did not, so a declared handler type accepted what
    ///     an inline one was refused.
    /// </remarks>
    [Fact]
    public void Checks_EventConnect_HandlerMayTakeFewerParametersThanTheEventDeclares() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                event abc(a: number, b: string);
                abc += fn(a) { print(a); };
                abc += fn() { print("fired"); };
                abc += fn(a, b) { print(a, b); };
                """
            )
        );

    [Fact]
    public void ThrowsFor_EventConnect_HandlerTakingMoreParametersThanTheEventDeclares()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("event abc(a: number); abc += fn(a: number, b: string) { };");

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type 'fn(number, string): void' is not assignable to type 'fn(number): void'."
        );
    }

    [Fact]
    public void Checks_VariadicEventConnect_HandlerMayNameEachArgument() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                event abc(..data: unknown[]);
                abc += fn(a, b, c) { print(a, b, c); };
                abc += fn(only) { print(only); };
                abc += fn() { };
                """
            )
        );

    [Fact]
    public void Checks_VariadicEventConnect_HandlerParametersInferTheRestElementType() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics("event abc(label: string, ..rest: number[]); abc += fn(label, first) { let l: string = label; let f: number = first; };")
        );

    [Fact]
    public void ThrowsFor_VariadicEventConnect_HandlerParameterMismatchesTheRestElementType()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("event abc(..data: number[]); abc += fn(a) { let s: string = a; };");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type 'number' is not assignable to type 'string'.");
    }

    [Fact]
    public void Checks_VariadicEventFire_AcceptsAnyArgumentCountAndASpread() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                event abc(label: string, ..rest: number[]);
                let ns = [1, 2];
                abc("hi");
                abc("hi", 1, 2);
                abc("hi", ..ns);
                """
            )
        );

    [Fact]
    public void ThrowsFor_VariadicEventFire_ArgumentMismatchesTheRestElementType()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("event abc(..data: number[]); abc(1, \"no\");");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '\"no\"' is not assignable to type 'number'.");
    }

    [Fact]
    public void ThrowsFor_EventConnect_AnonymousHandlerWithMoreParametersThanEventDeclares()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("event abc(x: number); abc += fn(x, extra) { };");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.MustHaveDefaultOrType,
            "Parameter must have a declared type or default value to infer from."
        );
    }

    [Fact]
    public void Checks_Parameter_MissingTypeAndDefault_FallsBackToUnknown_WithoutThrowing()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("fn foo(x) { x + 1 }");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidBinaryOp, "No binary operation for 'unknown' + 'number'.");
    }

    [Fact]
    public void Checks_InterfaceEventMember_Invocation_TypesAsVoid()
    {
        const string source = """
            interface EventObject {
                event consumer(param: string);
            }

            let eo = none as never as EventObject;
            eo.consumer("abc")
            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));
        var type = Utility.GetLastStatementType(source);
        Assert.True(type.Equals(PrimitiveType.Void), $"Expected 'void', got '{type}'");
    }

    [Fact]
    public void ThrowsFor_FiringConsumerEvent_ThroughInterfaceMember_AccessedThroughVariable()
    {
        const string source = """
            interface EventObject {
                mut consumer: ConsumerEvent<string>;
            }

            let eo = none as never as EventObject;
            eo.consumer("abc")
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.InvalidInvocation,
            "Consumer events may only be observed, not fired."
        );
    }

    [Fact]
    public void Checks_InterfaceEvent_WithAttribute_NoErrors() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                interface EventObject {
                    [luau_name("OnConsume")]
                    event consumer(param: string);
                }
                """
            )
        );

    [Fact]
    public void Checks_GlobalEvent_WithAttribute_NoErrors() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                [luau_name("OnConsume")]
                event consumer(param: string);
                """
            )
        );

    [Fact]
    public void ThrowsFor_InterfaceEvent_WithNonFunctionAttribute()
    {
        const string source = """
            let not_a_function = 1;
            interface EventObject {
                [not_a_function]
                event consumer(param: string);
            }
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.NonFunctionAttribute,
            "Only functions may be used as attributes."
        );

        Assert.Single(diagnostics.Set, d => d.Code == InternalCodes.NonFunctionAttribute);
    }

    [Fact]
    public void ThrowsFor_GlobalEvent_WithNonFunctionAttribute()
    {
        const string source = """
            let not_a_function = 1;
            [not_a_function]
            event consumer(param: string);
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.NonFunctionAttribute,
            "Only functions may be used as attributes."
        );

        Assert.Single(diagnostics.Set, d => d.Code == InternalCodes.NonFunctionAttribute);
    }

    [Fact]
    public void Checks_DeclareEvent_TypesAsConsumerEvent()
    {
        const string source = "declare event consumer(param: string);";
        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));

        var type = Utility.GetLastStatementType(source);
        var instantiated = Assert.IsType<InstantiatedType>(type);
        Assert.Equal("ConsumerEvent", instantiated.GenericType.Declaration.Name.Text);
        Assert.True(
            instantiated.Arguments.TakeWhile(Type.IsDefined).Single().Equals(PrimitiveType.String),
            $"Expected first event argument to be 'string', got '{instantiated.Arguments.FirstOrDefault()}'"
        );
    }

    [Fact]
    public void Checks_DeclareEvent_Connect_TypesAsEventConnection()
    {
        const string source = """
            declare event consumer(param: string);

            fn on_consumer(p: string): void { }
            consumer += on_consumer
            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));
        var type = Utility.GetLastStatementType(source);
        var interfaceType = Assert.IsType<InterfaceType>(type);
        Assert.Equal("ScriptConnection", interfaceType.Name);
    }

    [Fact]
    public void ThrowsFor_FiringConsumerEvent_ThroughDeclareEvent()
    {
        const string source = """
            declare event consumer(param: string);
            consumer("abc")
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.InvalidInvocation,
            "Consumer events may only be observed, not fired."
        );
    }

    [Fact]
    public void Checks_DeclareEvent_WithAttribute_NoErrors() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                [luau_name("OnConsume")]
                declare event consumer(param: string);
                """
            )
        );
}
