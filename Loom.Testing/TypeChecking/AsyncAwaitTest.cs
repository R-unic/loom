using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using FunctionType = Loom.Core.TypeChecking.Types.FunctionType;
using PrimitiveType = Loom.Core.TypeChecking.Types.PrimitiveType;
using Loom.Testing;

namespace Loom.Testing.TypeChecking;

[Collection("Assembly")]
public class AsyncAwaitTest
{
    [Fact]
    public void AnAsyncFunctionDeclarationCarriesItsModifier()
    {
        var tree = Utility.GetAST("async fn f(): number -> 1;");

        var declaration = Assert.IsType<FunctionDeclaration>(Assert.Single(tree.Statements));
        Assert.NotNull(declaration.AsyncKeyword);
        Assert.Equal("async", declaration.AsyncKeyword.Text);
    }

    [Fact]
    public void AsyncIsAcceptedWhereverAFunctionCanBegin()
    {
        Utility.AssertNoErrors(Utility.GetParserDiagnostics("export async fn f(): number -> 1;"));
        Utility.AssertNoErrors(Utility.GetParserDiagnostics("let f = async fn(): number -> 1;"));
        Utility.AssertNoErrors(Utility.GetParserDiagnostics("let f: async fn(): number = async fn(): number -> 1;"));
        Utility.AssertNoErrors(Utility.GetParserDiagnostics("declare async fn f(): number;"));
        Utility.AssertNoErrors(Utility.GetParserDiagnostics("[fallible] async fn f(): number -> 1;"));
    }

    [Fact]
    public void AsyncWithoutAFunctionIsReported()
    {
        var diagnostics = Utility.GetParserDiagnostics("async let x = 1;");
        Assert.Contains(diagnostics.Set, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    // 'await x?' has to mean '(await x)?': a Future is never a Result, so the other reading could only
    // ever be an error, and a Roblox member that yields usually raises too.
    [Fact]
    public void ErrorPropagationBindsOutsideAnAwait()
    {
        var tree = Utility.GetAST("async fn f(): number { let v = await g()?; }");
        var body = Assert.IsType<Block>(Assert.IsType<FunctionDeclaration>(Assert.Single(tree.Statements)).Body);
        var declaration = Assert.IsType<VariableDeclaration>(body.Statements[0]);

        var propagation = Assert.IsType<ErrorPropagation>(declaration.EqualsValueClause?.Value);
        Assert.IsType<Await>(propagation.Expression);
    }

    // everything other than '?' follows JS: the operand is the whole postfix chain, so reading a member
    // off an awaited value still takes parentheses
    [Fact]
    public void AwaitTakesTheWholePostfixChain()
    {
        var tree = Utility.GetAST("async fn f(): number { let v = await g().h; }");
        var body = Assert.IsType<Block>(Assert.IsType<FunctionDeclaration>(Assert.Single(tree.Statements)).Body);
        var declaration = Assert.IsType<VariableDeclaration>(body.Statements[0]);

        var await = Assert.IsType<Await>(declaration.EqualsValueClause?.Value);
        Assert.IsType<PropertyAccess>(await.Expression);
    }

    [Fact]
    public void AwaitingInsideANonAsyncFunctionIsReported()
    {
        const string source = """
            async fn load(): number -> 1;

            fn caller(): number {
                return await load();
            }
            """;

        Utility.AssertDiagnostic(
            Utility.GetAnalysisDiagnostics(source),
            InternalCodes.AwaitOutsideAsyncFunction,
            "'await' can only be used inside an 'async' function, and 'caller' is not one."
        );
    }

    [Fact]
    public void AwaitingAtModuleScopeIsReported()
    {
        const string source = """
            async fn load(): number -> 1;

            let value = await load();
            """;

        Utility.AssertDiagnostic(
            Utility.GetAnalysisDiagnostics(source),
            InternalCodes.AwaitOutsideAsyncFunction,
            "'await' can only be used inside an 'async' function."
        );
    }

    // an anonymous function has no signature for 'async' to appear on and no caller to propagate to -
    // an event handler runs on a thread Roblox owns and is free to yield
    [Fact]
    public void AwaitingInsideAFunctionExpressionIsAllowed()
    {
        const string source = """
            async fn load(): number -> 1;

            let handler = fn(): number -> await load();
            """;

        Utility.AssertNoErrors(Utility.GetAnalysisDiagnostics(source));
    }

    // What is being asserted here is that the call is not its declared return type;
    // AwaitingAFutureUnwrapsIt covers what is inside.
    [Fact]
    public void CallingAnAsyncFunctionEvaluatesToAFutureRatherThanItsReturnType()
    {
        const string source = """
            async fn load(): number -> 1;

            fn caller(): number -> load();
            """;

        Utility.AssertDiagnostic(
            Utility.GetTypeCheckerDiagnostics(source),
            InternalCodes.TypeMismatch,
            "Type 'Future<number>' is not assignable to type 'number'."
        );
    }

    [Fact]
    public void AwaitingAFutureUnwrapsIt()
    {
        const string source = """
            async fn load(): number -> 1;

            async fn caller(): string {
                return await load();
            }
            """;

        Utility.AssertDiagnostic(
            Utility.GetTypeCheckerDiagnostics(source),
            InternalCodes.TypeMismatch,
            "Type 'number' is not assignable to type 'string'."
        );
    }

    [Fact]
    public void AwaitingSomethingThatIsNotAFutureIsReported()
    {
        const string source = """
            async fn caller(): number {
                return await 1;
            }
            """;

        Utility.AssertDiagnostic(
            Utility.GetTypeCheckerDiagnostics(source),
            InternalCodes.AwaitRequiresFuture,
            "'await' can only be used on a 'Future<T>', but got '1'."
        );
    }

    // a future read back out of a variable used to arrive as the expanded interface rather than as the
    // instantiation, with the T 'await' hands back already gone - see #198
    [Fact]
    public void AFutureHeldInAVariableCanStillBeAwaited()
    {
        const string source = """
            async fn load(): number -> 1;

            async fn caller(): string {
                let pending = load();
                return await pending;
            }
            """;

        Utility.AssertDiagnostic(
            Utility.GetTypeCheckerDiagnostics(source),
            InternalCodes.TypeMismatch,
            "Type 'number' is not assignable to type 'string'."
        );
    }

    [Fact]
    public void PropagatingThroughAnUnawaitedCallIsReported()
    {
        const string source = """
            async fn load(): Result<number, string> -> Result::ok(1);

            async fn caller(): Result<number, string> {
                let value = load()?;
                return Result::ok(value);
            }
            """;

        Utility.AssertDiagnostic(
            Utility.GetTypeCheckerDiagnostics(source),
            InternalCodes.UnawaitedFutureAccess,
            "The '?' operator cannot be used on 'Future<Result<number, string>>' - the Result is inside the future, not on it.",
            "await the call first: 'await expression?' already means '(await expression)?'"
        );
    }

    [Fact]
    public void AnAsyncFunctionIsNotAssignableWhereASynchronousOneIsExpected()
    {
        const string source = """
            async fn load(): number -> 1;

            fn take(compute: fn(): number): number -> compute();

            let n = take(load);
            """;

        Assert.Contains(
            Utility.GetTypeCheckerDiagnostics(source).Set,
            diagnostic => diagnostic.Code == InternalCodes.TypeMismatch
        );
    }

    [Fact]
    public void AsyncnessIsPartOfTheFunctionType()
    {
        var asynchronous = new FunctionType([], [], PrimitiveType.Number, false, true);
        var synchronous = new FunctionType([], [], PrimitiveType.Number);

        Assert.False(asynchronous.Equals(synchronous));
        Assert.False(synchronous.Equals(asynchronous));
        Assert.False(asynchronous.IsAssignableTo(synchronous));
        Assert.False(synchronous.IsAssignableTo(asynchronous));
        Assert.Equal("async fn(): number", asynchronous.ToString());
        Assert.Equal("fn(): number", synchronous.ToString());
    }

    [Fact]
    public void AnUnawaitedCallBuildsAFutureAndAnAwaitedOneDoesNot()
    {
        const string source = """
            async fn load(key: string): number -> 1;

            async fn caller(): number {
                let pending = load("a");
                return await load("b") + await pending;
            }
            """;

        var luau = Utility.GetLuauAST(source, true).Render();

        Assert.Contains("Loom.future(load, \"a\")", luau, StringComparison.Ordinal);
        Assert.Contains("load(\"b\")", luau, StringComparison.Ordinal);
        Assert.DoesNotContain("Loom.future(load, \"b\")", luau, StringComparison.Ordinal);
        Assert.Contains("Loom.await(pending)", luau, StringComparison.Ordinal);
    }

    // a yielding Roblox member is 'async fn' like any other, so an awaited call is the plain Luau call
    // and an unawaited one is spawned with its receiver passed explicitly
    [Fact]
    public void AYieldingRobloxMemberLowersLikeAnyOtherAsyncCall()
    {
        const string source = """
            async fn find(instance: Instance): Instance {
                return await instance.wait_for_child("Torso");
            }

            fn later(instance: Instance): void {
                let pending = instance.wait_for_child("Torso");
            }
            """;

        var luau = Utility.GetLuauAST(source, true).Render();

        Assert.Contains("instance:WaitForChild(\"Torso\")", luau, StringComparison.Ordinal);
        Assert.Contains("Loom.future(", luau, StringComparison.Ordinal);
    }

    // 'await …?' on a member that both yields and raises should emit neither a future nor a Result: the
    // await fuses into the call and the '?' fuses into the xpcall
    [Fact]
    public void AwaitingAndPropagatingAFallibleYieldingMemberAllocatesNeither()
    {
        const string source = """
            let data_store_service = get_service::<DataStoreService>();

            async fn load(key: string): Result<unknown, RobloxError> {
                let store = data_store_service.get_data_store("players")?;
                let value = await store.get_async(key)?;
                return Result::ok(value);
            }
            """;

        var luau = Utility.GetLuauAST(source, true).Render();

        Assert.Contains("xpcall(store.GetAsync, Loom.roblox_error, store, key)", luau, StringComparison.Ordinal);
        Assert.DoesNotContain("Loom.future", luau, StringComparison.Ordinal);
        Assert.DoesNotContain("Loom.await", luau, StringComparison.Ordinal);
    }

    // a member that both yields and raises, started without being awaited: the xpcall has to happen on the
    // future's own thread, so the raise becomes the Result its type already promises instead of rejecting
    [Fact]
    public void StartingAFallibleYieldingMemberWithoutAwaitingItWrapsOnItsOwnThread()
    {
        const string source = """
            let data_store_service = get_service::<DataStoreService>();

            [fallible]
            fn start(key: string): void {
                let store = data_store_service.get_data_store("players").unwrap();
                let pending = store.get_async(key);
            }
            """;

        var luau = Utility.GetLuauAST(source, true).Render();

        Assert.Contains("Loom.future_wrapped(", luau, StringComparison.Ordinal);
        Assert.Contains(".GetAsync", luau, StringComparison.Ordinal);
    }

    // the one part of Roblox's 'task' library the language does not already cover - 'delay' is what
    // 'after' lowers to, 'spawn' is what calling an async fn does, and 'every' covers the rest
    [Fact]
    public void WaitIsAsyncAndLowersToTaskWait()
    {
        const string source = """
            async fn pause(): number {
                return await wait(1);
            }
            """;

        var luau = Utility.GetLuauAST(source, true).Render();

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));
        Assert.Contains("task.wait(1)", luau, StringComparison.Ordinal);
        Assert.DoesNotContain("Loom.future", luau, StringComparison.Ordinal);
    }

    [Fact]
    public void WaitingWithoutAwaitingIsAFuture()
    {
        const string source = "fn caller(): number -> wait(1);";

        Utility.AssertDiagnostic(
            Utility.GetTypeCheckerDiagnostics(source),
            InternalCodes.TypeMismatch,
            "Type 'Future<number>' is not assignable to type 'number'."
        );
    }

    // one 'await' covers a whole chain of yielding calls, so a wait_for_child chain does not need a set of
    // parentheses per link - every step is fused, so the chain collapses to the plain Luau calls
    [Fact]
    public void OneAwaitCoversAChainOfYieldingCalls()
    {
        const string source = """
            async fn find(a: Instance): Instance {
                return await a.wait_for_child("a").wait_for_child("b").wait_for_child("c");
            }
            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));

        var luau = Utility.GetLuauAST(source, true).Render();

        Assert.Contains("a:WaitForChild(\"a\"):WaitForChild(\"b\"):WaitForChild(\"c\")", luau, StringComparison.Ordinal);
        Assert.DoesNotContain("Loom.future", luau, StringComparison.Ordinal);
        Assert.DoesNotContain("Loom.await", luau, StringComparison.Ordinal);
    }

    // the read-through is tied to "another suspension point follows", so a field read off a future still
    // takes the parenthesised form - which is also what keeps 'future.status' meaning what it says
    [Fact]
    public void ReadingAFieldOffAFutureStillNeedsParentheses()
    {
        const string source = """
            async fn find(a: Instance): string {
                return await a.wait_for_child("a").name;
            }
            """;

        Utility.AssertDiagnostic(
            Utility.GetTypeCheckerDiagnostics(source),
            InternalCodes.UnawaitedFutureAccess,
            "Cannot access property 'name' on type 'Future<Instance>' - it belongs to the awaited value, not to the future.",
            "write '(await ...).name', or await the call if 'name' is the next step of a chain"
        );

        const string parenthesised = """
            async fn find(a: Instance): string {
                return (await a.wait_for_child("a")).name;
            }
            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(parenthesised));
    }

    // a future's own members stay reachable without an await - reading 'status' is how you poll one
    [Fact]
    public void AFuturesOwnMembersAreStillReadableWithoutAwaiting()
    {
        const string source = """
            async fn load(): number -> 1;

            fn poll(): FutureStatus {
                let pending = load();
                return pending.status;
            }
            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));
    }

    [Fact]
    public void AChainWithoutAnAwaitIsStillReported()
    {
        const string source = """
            fn find(a: Instance): void {
                let child = a.wait_for_child("a").wait_for_child("b");
            }
            """;

        Assert.Contains(
            Utility.GetTypeCheckerDiagnostics(source).Set,
            diagnostic => diagnostic.Code == InternalCodes.UnawaitedFutureAccess
        );
    }

    // and writing the annotation out does not change the answer - the declared type went through the same
    // expansion the inferred one did (#198)
    [Fact]
    public void AnAnnotatedFutureVariableCanStillBeAwaited()
    {
        const string source = """
            async fn produce(): number { return 1; }
            async fn consume(): number {
                let pending: Future<number> = produce();
                return await pending;
            }
            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));
    }

    // a future that arrived some other way is not a call the fusion collapsed, so it has to actually be
    // waited for before the member exists on it
    [Fact]
    public void AStoredFutureInAChainIsWaitedForProperly()
    {
        const string source = """
            async fn find(a: Instance): Instance {
                let pending = a.wait_for_child("a");
                return await pending.wait_for_child("b");
            }
            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));

        var luau = Utility.GetLuauAST(source, true).Render();

        Assert.Contains("Loom.await(pending)", luau, StringComparison.Ordinal);
    }

    // only the awaited expression's own spine reads through - a chain buried in an argument is not quietly
    // awaited along with it, even though it sits inside the await's subtree
    [Fact]
    public void AChainInsideAnArgumentDoesNotReadThrough()
    {
        const string source = """
            async fn caller(a: Instance, b: Instance): Instance {
                return await a.wait_for_child(b.wait_for_child("x").get_full_name());
            }
            """;

        Utility.AssertDiagnostic(
            Utility.GetTypeCheckerDiagnostics(source),
            InternalCodes.UnawaitedFutureAccess,
            "Cannot access property 'get_full_name' on type 'Future<Instance>' - it belongs to the awaited value, not to the future.",
            "write '(await ...).get_full_name', or await the call if 'get_full_name' is the next step of a chain"
        );
    }

    [Fact]
    public void TheFutureStaticsRouteToTheRuntime()
    {
        const string source = """
            async fn load(): number -> 1;

            async fn caller(): number[] {
                return await Future::all([load(), load()]);
            }
            """;

        var luau = Utility.GetLuauAST(source, true).Render();

        Assert.Contains("Loom.future_all(", luau, StringComparison.Ordinal);
        Assert.Contains("Loom.await(", luau, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Luau raises rather than suspends when a thread yields across a C-call boundary, so a function
    ///     standing in one says so and the compiler holds it to that.
    /// </summary>
    [Fact]
    public void AwaitingInsideANoYieldFunctionIsReported()
    {
        const string source = """
            async fn load(): number -> 1;

            [no_yield]
            fn compare(): number {
                return await load();
            }
            """;

        Utility.AssertDiagnostic(
            Utility.GetAnalysisDiagnostics(source),
            InternalCodes.YieldInNoYieldContext,
            "'compare' is marked '[no_yield]', so it cannot await."
        );
    }

    /// <remarks>The attribute is what makes this an error - an ordinary function awaiting is a different one.</remarks>
    [Fact]
    public void AwaitingInsideANoYieldFunctionOutranksTheAsyncDiagnostic()
    {
        const string source = """
            async fn load(): number -> 1;

            [no_yield]
            fn compare(): number {
                return await load();
            }
            """;

        var diagnostics = Utility.GetAnalysisDiagnostics(source);

        Assert.DoesNotContain(diagnostics.Set, diagnostic => diagnostic.Code == InternalCodes.AwaitOutsideAsyncFunction);
    }

    [Fact]
    public void AFunctionCannotBeBothAsyncAndNoYield()
    {
        const string source = """
            [no_yield]
            async fn compare(): number -> 1;
            """;

        Utility.AssertDiagnostic(
            Utility.GetAnalysisDiagnostics(source),
            InternalCodes.YieldInNoYieldContext,
            "'compare' is both 'async' and '[no_yield]'."
        );
    }

    /// <remarks>
    ///     A function expression runs on a thread of its own, which is what exempts it from needing 'async'
    ///     at all - so it does not inherit the surrounding function's promise not to yield either.
    /// </remarks>
    [Fact]
    public void AwaitingInsideAFunctionExpressionWithinANoYieldFunctionIsAllowed()
    {
        const string source = """
            async fn load(): number -> 1;

            [no_yield]
            fn compare(): number {
                let handler = fn(): number -> await load();
                return 1;
            }
            """;

        Utility.AssertNoErrors(Utility.GetAnalysisDiagnostics(source));
    }
}
