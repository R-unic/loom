using Loom.Core.Diagnostics;
using Loom.Core.TypeChecking;
using Loom.Core.TypeChecking.Types;
using AstConditionalType = Loom.Core.Parsing.AST.ConditionalType;
using InferType = Loom.Core.Parsing.AST.InferType;
using TypeMatch = Loom.Core.Parsing.AST.TypeMatch;
using TypePredicateType = Loom.Core.Parsing.AST.TypePredicateType;
using WildcardType = Loom.Core.Parsing.AST.WildcardType;
using LoomType = Loom.Core.TypeChecking.Types.Type;

namespace Loom.Testing;

/// <summary>
///     <c>T is U ? A : B</c> and the n-armed <c>match</c> it is the two-armed case of, plus the <c>let</c>
///     binders and <c>each</c> distribution the utility types built on them need. Issue #202.
/// </summary>
[Collection("Assembly")]
public class ConditionalTypeTest
{
    private const string Utilities = """
        type ReturnType<T> = T is fn(..unknown[]): let R ? R : never;
        type ElementOf<T> = T is (let E)[] ? E : never;
        type Exclude<T, U> = match each T {
            U -> never,
            let Other -> Other,
        };
        type Extract<T, U> = match each T {
            U -> U,
            _ -> never,
        };
        type Awaited<T> = match T {
            Future<let V> -> Awaited<V>,
            _ -> T,
        };
        type Parameters<T> = match T {
            fn(..let P): unknown -> P,
            _ -> never,
        };


        """;

    /// <summary>
    ///     What <paramref name="alias" /> works out to, structurally: a resolved conditional is bound under
    ///     the name it was written as, so <see cref="TypeSimplifier.Expanded" /> is what asks for the answer
    ///     rather than the name.
    /// </summary>
    private static string TypeOfAlias(string source, string alias) =>
        TypeSimplifier.Expanded(Utility.GetLastStatementType($"{source}\ndeclare let value: {alias};\nvalue")).ToString();

    [Fact]
    public void Parses_TheTernaryForm()
    {
        var tree = Utility.GetAST("type X = C is true ? number : string;");
        var conditional = Assert.Single(tree.GetDescendants<AstConditionalType>());

        Assert.Empty(tree.GetDescendants<TypePredicateType>());
        Assert.Equal("number", conditional.ThenType.Tokens[0].Text);
        Assert.Equal("string", conditional.ElseType.Tokens[0].Text);
    }

    /// <summary>
    ///     The same <c>x is T</c> shape a function's return position takes still reads as a predicate: the
    ///     <c>?</c> is the only thing telling the two apart, and it is not there.
    /// </summary>
    [Fact]
    public void Parses_ATypePredicate_Unchanged()
    {
        var tree = Utility.GetAST("declare fn is_text(value: unknown): value is string;");

        Assert.Single(tree.GetDescendants<TypePredicateType>());
        Assert.Empty(tree.GetDescendants<AstConditionalType>());
    }

    /// <summary>
    ///     A <c>?</c> directly on the <c>is</c> target is the branch, not an optional type - which is why a
    ///     target that really is optional has to be parenthesized.
    /// </summary>
    [Fact]
    public void Parses_TheQuestionMarkAsTheBranch_NotAnOptionalTarget()
    {
        Utility.AssertNoErrors(Utility.GetParserDiagnostics("type X<T> = T is number ? true : false;"));
        Utility.AssertNoErrors(Utility.GetParserDiagnostics("type X<T> = T is (number?) ? true : false;"));
    }

    /// <summary>
    ///     The <c>?</c> follows the <em>return type</em> of the target, which is the last thing parsed for
    ///     it - so suppressing the optional suffix has to reach that far in and no further.
    /// </summary>
    [Fact]
    public void Parses_ABinderInAFunctionTypesReturnPosition()
    {
        var tree = Utility.GetAST("type ReturnType<T> = T is fn(..unknown[]): let R ? R : never;");

        Utility.AssertNoErrors(Utility.GetParserDiagnostics("type ReturnType<T> = T is fn(..unknown[]): let R ? R : never;"));
        Assert.Single(tree.GetDescendants<AstConditionalType>());
        Assert.Equal("R", Assert.Single(tree.GetDescendants<InferType>()).Name.Text);
    }

    [Fact]
    public void Parses_TheMatchForm_AndItsWildcard()
    {
        var tree = Utility.GetAST("type X<T> = match T { number -> true, _ -> false };");
        var match = Assert.Single(tree.GetDescendants<TypeMatch>());

        Assert.Null(match.EachKeyword);
        Assert.Equal(2, match.Arms.Count);
        Assert.Single(tree.GetDescendants<WildcardType>());
    }

    [Fact]
    public void Parses_EachAsDistribution()
    {
        var tree = Utility.GetAST("type X<T> = match each T { number -> true, _ -> false };");
        Assert.NotNull(Assert.Single(tree.GetDescendants<TypeMatch>()).EachKeyword);
    }

    [Fact]
    public void Reports_AMatchWithNoArms()
    {
        var diagnostics = Utility.GetParserDiagnostics("type X<T> = match T { };");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.EmptyMatch, "Type-level 'match' needs at least one arm.");
    }

    /// <summary>
    ///     <c>let</c> and <c>_</c> only mean anything inside a pattern; outside one they are the ordinary
    ///     keyword and an ordinary name.
    /// </summary>
    [Fact]
    public void Reports_ABinderOutsideAPattern() => Assert.NotEmpty(Utility.GetParserDiagnostics("type X = let R;").Set);

    /// <summary>
    ///     Without the <c>?</c> this is the predicate form, whose subject is a parameter name - so anything
    ///     else there is a conditional missing its branches rather than a predicate with an odd subject.
    /// </summary>
    [Fact]
    public void Reports_AnIsTypeThatIsNeitherFormCleanly()
    {
        var diagnostics = Utility.GetParserDiagnostics("type X = (number) is string;");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.InvalidTypePredicateSubject,
            "Type predicate subject must be a parameter name.",
            "add '? ... : ...' after the type to write a conditional type instead"
        );
    }

    [Fact]
    public void Reports_TwoBindersOfTheSameName_InOnePattern()
    {
        var diagnostics = Utility.GetAnalysisDiagnostics("type X<T> = T is fn(let A, let A): void ? A : never;");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.DuplicateName, "Type 'A' is already declared in this scope.");
    }

    /// <summary>A binder is in scope for the branch its pattern chose, and nowhere else.</summary>
    [Fact]
    public void Reports_ABinderUsedInTheElseBranch()
    {
        var diagnostics = Utility.GetAnalysisDiagnostics("type X<T> = T is (let E)[] ? E : E;");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindName, "Cannot find type 'E'.");
    }

    [Fact]
    public void Reports_ABinderUsedInAnotherArm()
    {
        var diagnostics = Utility.GetAnalysisDiagnostics("type X<T> = match T { (let E)[] -> E, _ -> E };");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindName, "Cannot find type 'E'.");
    }

    /// <summary>Two arms may reuse a name: each one's binders belong to that arm alone.</summary>
    [Fact]
    public void Allows_TwoArmsToReuseABinderName() => Utility.AssertNoErrors(Utility.GetAnalysisDiagnostics("type X<T> = match T { (let E)[] -> E, Future<let E> -> E, _ -> never };"));

    [Fact]
    public void Answers_TheTernaryFormOnceItsSubjectIsConcrete()
    {
        Assert.Equal("number", TypeOfAlias("type C = true;\ntype X = C is true ? number : string;", "X"));
        Assert.Equal("string", TypeOfAlias("type C = false;\ntype X = C is true ? number : string;", "X"));
    }

    [Fact]
    public void Answers_TheArmsInOrder()
    {
        const string source = """
            type Describe<T> = match T {
                number -> "number",
                string -> "string",
                _ -> "other",
            };
            """;

        Assert.Equal("\"number\"", TypeOfAlias(source, "Describe<number>"));
        Assert.Equal("\"string\"", TypeOfAlias(source, "Describe<string>"));
        Assert.Equal("\"other\"", TypeOfAlias(source, "Describe<bool>"));
    }

    [Fact]
    public void Binds_AReturnTypeOutOfAFunction()
    {
        Assert.Equal("number", TypeOfAlias(Utilities, "ReturnType<fn(): number>"));
        Assert.Equal("number", TypeOfAlias(Utilities, "ReturnType<fn(a: string, b: bool): number>"));
        Assert.Equal("never", TypeOfAlias(Utilities, "ReturnType<number>"));
    }

    [Fact]
    public void Binds_AnElementTypeOutOfAnArray()
    {
        Assert.Equal("string", TypeOfAlias(Utilities, "ElementOf<string[]>"));
        Assert.Equal("never", TypeOfAlias(Utilities, "ElementOf<string>"));
    }

    /// <summary>
    ///     A function's parameters are a pack, which Loom already writes as a tuple - so <c>..let P</c>
    ///     binds the whole remainder of the signature rather than one parameter of it.
    /// </summary>
    [Fact]
    public void Binds_AParameterListAsATuple()
    {
        Assert.Equal("(number, string)", TypeOfAlias(Utilities, "Parameters<fn(a: number, b: string): void>"));
        Assert.Equal("()", TypeOfAlias(Utilities, "Parameters<fn(): void>"));
    }

    [Fact]
    public void Binds_ThroughAnOptionalAndATuple()
    {
        const string source = """
            type Defined<T> = match T { (let E)? -> E, _ -> never };
            type First<T> = match T { (let A, let B) -> A, _ -> never };
            """;

        Assert.Equal("string", TypeOfAlias(source, "Defined<string?>"));
        Assert.Equal("number", TypeOfAlias(source, "First<(number, string)>"));
        Assert.Equal("never", TypeOfAlias(source, "First<(number, string, bool)>"));
    }

    /// <summary>Positions the pattern pins down have to agree, not only the ones it binds.</summary>
    [Fact]
    public void Rejects_APositionThePatternPinsDown()
    {
        const string source = """
            type Second<T> = match T { (number, let B) -> B, _ -> never };
            type Waited<T> = match T { Future<let V> -> V, _ -> never };
            """;

        Assert.Equal("bool", TypeOfAlias(source, "Second<(number, bool)>"));
        Assert.Equal("never", TypeOfAlias(source, "Second<(string, bool)>"));
        Assert.Equal("number", TypeOfAlias(source, "Waited<Future<number>>"));
    }

    /// <summary>
    ///     A subject written as some other alias is expanded before the arms see it - the pattern is asking
    ///     about its shape, and the name it was reached through is not part of that.
    /// </summary>
    [Fact]
    public void Binds_ThroughASubjectWrittenAsAnotherAlias() => Assert.Equal("number", TypeOfAlias(Utilities, "ElementOf<Array<number>>"));

    /// <summary>
    ///     A union's members have no order anybody wrote, so a pattern finds each member a partner rather
    ///     than pairing them off by position.
    /// </summary>
    [Fact]
    public void Binds_AUnionMember_WhicheverOrderItIsWrittenIn()
    {
        const string source = "type Other<T> = match T { number | let R -> R, _ -> never };";

        Assert.Equal("string", TypeOfAlias(source, "Other<number | string>"));
        Assert.Equal("string", TypeOfAlias(source, "Other<string | number>"));
        Assert.Equal("never", TypeOfAlias(source, "Other<string | bool>"));
    }

    /// <summary>One name twice in a pattern means the two positions must agree, rather than widening to their union.</summary>
    [Fact]
    public void Requires_ARepeatedBinderToAgree()
    {
        const string source = "type Same<T> = match T { fn(a: let A, b: let A): unknown -> A, _ -> never };";

        Assert.Equal("number", TypeOfAlias(source, "Same<fn(a: number, b: number): void>"));
        Assert.Equal("never", TypeOfAlias(source, "Same<fn(a: number, b: string): void>"));
    }

    [Fact]
    public void Rejects_AFunctionPatternOfTheWrongShape()
    {
        const string source = """
            type Second<T> = match T { fn(a: unknown, b: let B): unknown -> B, _ -> never };
            type Waited<T> = match T { async fn(): let R -> R, _ -> never };
            """;

        Assert.Equal("string", TypeOfAlias(source, "Second<fn(a: number, b: string): void>"));
        Assert.Equal("never", TypeOfAlias(source, "Second<fn(a: number): void>"));
        Assert.Equal("never", TypeOfAlias(source, "Waited<fn(): number>"));
    }

    /// <summary>
    ///     A partner that binds and then fails further in has to put back what it bound, since a later
    ///     member of the union may still take it.
    /// </summary>
    [Fact]
    public void Unbinds_APartialMatchThatFailed()
    {
        const string source = """
            type Handler = (fn(a: number): bool) | number;
            type Arg<T> = match T { (fn(a: let A): string) | number -> A, _ -> never };
            type Kept<T> = match T { (fn(a: let A): bool) | number -> A, _ -> never };
            """;

        Assert.Equal("never", TypeOfAlias(source, "Arg<Handler>"));
        Assert.Equal("number", TypeOfAlias(source, "Kept<Handler>"));
    }

    /// <summary>Whatever an abandoned partner bound is put back, so a name the earlier members took keeps its binding.</summary>
    [Fact]
    public void Unbinds_AFailedPartner_WithoutLosingEarlierBindings()
    {
        const string source = """
            type Handler = number[] | (fn(a: number): bool);
            type Pair<T> = match T { (let B)[] | (fn(a: let A): string) -> B, _ -> never };
            """;

        Assert.Equal("never", TypeOfAlias(source, "Pair<Handler>"));
    }

    /// <summary>
    ///     A rest binder takes the whole remainder as one pack, so a constraint on it is a constraint on the
    ///     pack rather than on each parameter - and nothing that is not a tuple satisfies one.
    /// </summary>
    [Fact]
    public void Rejects_ARestBinderWhoseConstraintTheWholePackCannotMeet()
    {
        const string source = "type Ps<T> = match T { fn(..let P: number): unknown -> P, _ -> never };";

        Assert.Equal("never", TypeOfAlias(source, "Ps<fn(a: number): void>"));
    }

    /// <summary>A rest written as an element array is a shape every remaining parameter has to fit.</summary>
    [Fact]
    public void Rejects_AParameterThatDoesNotFitTheRestElement()
    {
        const string source = "type NumericReturn<T> = T is fn(..number[]): let X ? X : never;";

        Assert.Equal("bool", TypeOfAlias(source, "NumericReturn<fn(a: number, b: number): bool>"));
        Assert.Equal("never", TypeOfAlias(source, "NumericReturn<fn(a: string): bool>"));
    }

    /// <summary>Nothing to distribute over when the subject is not a union - <c>each</c> then means nothing at all.</summary>
    [Fact]
    public void Distributes_OverANonUnion_AsItself() => Assert.Equal("number", TypeOfAlias(Utilities, "Exclude<number, string>"));

    /// <summary>An intersection is unknown while any part of it is, the same way a union is.</summary>
    [Fact]
    public void Defers_WhileAnIntersectionSubjectHoldsAParameter()
    {
        var type = Utility.GetLastStatementType(
            """
            interface Point { x: number; }
            type Describe<T> = match T { unknown -> true, _ -> false };
            declare fn describe<T>(value: Describe<Point & T>): Describe<Point & T>;
            describe
            """
        );

        var function = Assert.IsType<FunctionType>(type);
        Assert.IsType<ConditionalType>(TypeSimplifier.Expanded(function.ReturnType));
    }

    /// <summary>A union pattern describes the whole union, so one with a different number of members is a different type.</summary>
    [Fact]
    public void Rejects_AUnionPatternOfADifferentWidth() => Assert.Equal("never", TypeOfAlias("type Other<T> = match T { number | let R -> R, _ -> never };", "Other<number | string | bool>"));

    /// <summary>An arm list with no catch-all narrows to nothing when nothing matches.</summary>
    [Fact]
    public void Answers_Never_WhenNoArmMatches() => Assert.Equal("never", TypeOfAlias("type OnlyNumber<T> = match T { number -> true };", "OnlyNumber<string>"));

    /// <summary>Distributing runs the arms once per member, and <c>never</c> is the union with no members.</summary>
    [Fact]
    public void Distributes_OverNever_AsNever() => Assert.Equal("never", TypeOfAlias(Utilities, "Exclude<never, string>"));

    [Fact]
    public void Distributes_OverAUnion_OnlyWithEach()
    {
        Assert.Equal("\"a\" | \"c\"", TypeOfAlias(Utilities, "Exclude<\"a\" | \"b\" | \"c\", \"b\">"));
        Assert.Equal("\"b\"", TypeOfAlias(Utilities, "Extract<\"a\" | \"b\" | \"c\", \"b\">"));
    }

    /// <summary>
    ///     Without <c>each</c> the subject is one whole type, so a union is measured against the arms as
    ///     itself rather than a member at a time. That difference is the whole reason <c>each</c> is written
    ///     rather than inferred.
    /// </summary>
    [Fact]
    public void Answers_AUnionAsOneWholeType_WithoutEach()
    {
        const string source = "type IsNumber<T> = T is number ? true : false;";

        Assert.Equal("true", TypeOfAlias(source, "IsNumber<number>"));
        Assert.Equal("false", TypeOfAlias(source, "IsNumber<number | string>"));
    }

    /// <summary>
    ///     An optional is a union with <c>none</c>, so distribution reaches both halves - which is what
    ///     makes a NonNullable out of the same three lines every other filter is written in.
    /// </summary>
    [Fact]
    public void Distributes_OverAnOptional()
    {
        const string source = """
            type NonNil<T> = match each T {
                none -> never,
                let U -> U,
            };
            """;

        Assert.Equal("string", TypeOfAlias(source, "NonNil<string?>"));
        Assert.Equal("number | string", TypeOfAlias(source, "NonNil<number | string>"));
    }

    /// <summary>
    ///     <c>Awaited</c> is self-referential on purpose, and unrolls iteratively rather than nesting - the
    ///     shape nearly every recursive utility type takes.
    /// </summary>
    [Fact]
    public void Resolves_ARecursionThroughAnArm()
    {
        Assert.Equal("number", TypeOfAlias(Utilities, "Awaited<Future<number>>"));
        Assert.Equal("number", TypeOfAlias(Utilities, "Awaited<Future<Future<Future<number>>>>"));
        Assert.Equal("number", TypeOfAlias(Utilities, "Awaited<number>"));
    }

    /// <summary>
    ///     A recursion producing a genuinely new argument every step is what the bounds exist for, and
    ///     exceeding one is a program error rather than a degraded type.
    /// </summary>
    [Fact]
    public void Reports_ARecursionThatNeverStops()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            type Loop<T> = match T {
                let U -> Loop<U[]>,
            };

            declare let value: Loop<number>;
            """
        );

        Assert.Contains(diagnostics.Set, d => d.Code == InternalCodes.ConditionalTypeTooDeep);
    }

    /// <summary>
    ///     Two aliases unrolling into each other close a loop on the same instantiation, which is caught by
    ///     recognising the repeat rather than by exhausting the iteration bound.
    /// </summary>
    [Fact]
    public void Reports_TwoAliasesThatUnrollIntoEachOther()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            type Ping<T> = match T { let U -> Pong<U> };
            type Pong<T> = match T { let U -> Ping<U> };

            declare let value: Ping<number>;
            """
        );

        Assert.Contains(diagnostics.Set, d => d.Code == InternalCodes.ConditionalTypeTooDeep);
    }

    /// <summary>A binder written with a constraint only binds where the constraint holds.</summary>
    [Fact]
    public void Honours_AConstrainedBinder()
    {
        const string source = "type NumericElement<T> = T is (let E: number)[] ? E : never;";

        Assert.Equal("number", TypeOfAlias(source, "NumericElement<number[]>"));
        Assert.Equal("never", TypeOfAlias(source, "NumericElement<string[]>"));
    }

    /// <summary>Nothing to answer while the subject is a parameter, so it stays a <see cref="ConditionalType" />.</summary>
    [Fact]
    public void Defers_WhileTheSubjectIsStillAParameter()
    {
        var type = Utility.GetLastStatementType(
            Utilities
            + """
              declare fn identity<T>(value: ReturnType<T>): ReturnType<T>;
              identity
              """
        );

        var function = Assert.IsType<FunctionType>(type);
        Assert.IsType<ConditionalType>(TypeSimplifier.Expanded(function.ReturnType));
    }

    [Fact]
    public void ConditionalType_ToString()
    {
        var parameter = new TypeParameter("T");
        var ternary = new ConditionalType(
            parameter,
            [new ConditionalArm(PrimitiveType.Number, PrimitiveType.String, []), new ConditionalArm(PrimitiveType.Unknown, PrimitiveType.Bool, [])],
            false
        );

        var distributed = new ConditionalType(parameter, [new ConditionalArm(PrimitiveType.Number, PrimitiveType.String, [])], true);

        Assert.Equal("T is number ? string : bool", ternary.ToString());
        Assert.Equal("match each T { number -> string }", distributed.ToString());
    }

    /// <summary>
    ///     The nesting bound, which the tail-call loop deliberately does not consume - a recursion that grows
    ///     the type on every step nests instead, and TypeScript keeps that bound small. Built here rather
    ///     than written in Loom because reaching fifty levels from source takes a program nobody would write.
    /// </summary>
    [Fact]
    public void Reports_ARecursionThatNestsTooDeeply()
    {
        var binder = new TypeParameter("U");
        LoomType nested = PrimitiveType.Number;
        for (var depth = 0; depth < 60; depth++)
            nested = new ConditionalType(nested, [new ConditionalArm(binder, new ArrayType(binder, false), [binder])], false);

        ConditionalTypeEvaluator.ClearOverflow();
        Assert.Null(ConditionalTypeEvaluator.TryEvaluate((ConditionalType)nested));
        Assert.True(ConditionalTypeEvaluator.TookOverflow());
    }

    [Fact]
    public void ConditionalType_Equals_ComparesArmCount()
    {
        var parameter = new TypeParameter("T");
        var one = new ConditionalType(parameter, [new ConditionalArm(PrimitiveType.Number, PrimitiveType.String, [])], false);
        var two = new ConditionalType(
            parameter,
            [new ConditionalArm(PrimitiveType.Number, PrimitiveType.String, []), new ConditionalArm(PrimitiveType.Unknown, PrimitiveType.Bool, [])],
            false
        );

        Assert.NotEqual(one, two);
    }

    [Fact]
    public void ConditionalType_Equals()
    {
        var parameter = new TypeParameter("T");
        List<ConditionalArm> arms = [new(PrimitiveType.Number, PrimitiveType.String, [])];
        var first = new ConditionalType(parameter, arms, false);
        var second = new ConditionalType(parameter, [new ConditionalArm(PrimitiveType.Number, PrimitiveType.String, [])], false);
        var distributed = new ConditionalType(parameter, arms, true);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, distributed);
        Assert.NotEqual(first, new ConditionalType(parameter, [new ConditionalArm(PrimitiveType.String, PrimitiveType.String, [])], false));
    }

    /// <summary>
    ///     Tier one of the lowering: Luau can express the answer but not the question, so an instantiation
    ///     that resolved emits what it resolved to and no type function is involved.
    /// </summary>
    [Fact]
    public void Generates_TheAnswer_WhereTheSubjectWasConcrete()
    {
        var luau = Utility.GetLuauAST(Utilities + "declare let value: ReturnType<fn(): number>;\nlet copy = value;", true).Render();

        Assert.Contains("type ReturnType<T> = unknown", luau);
        Assert.DoesNotContain("ReturnType<", luau.Replace("type ReturnType<T>", ""));
    }

    /// <summary>
    ///     Every shape an answer can take, written back out as Luau. The generator otherwise walks the
    ///     syntax rather than the types, so this is the one path where a type has to be rendered from
    ///     nothing but itself.
    /// </summary>
    [Theory]
    [InlineData("T is number ? T? : never", "number?")]
    [InlineData("T is number ? T[] : never", "{ number }")]
    [InlineData("T is number ? fn(a: T): T : never", "(number) -> number")]
    [InlineData("T is number ? fn(..xs: T[]): void : never", "(...number) -> nil")]
    [InlineData("T is number ? Point : never", "Point")]
    [InlineData("T is number ? Result<T, string> : never", "Loom.Result<number, string>")]
    [InlineData("T is number ? u8 : never", "number")]
    [InlineData("T is number ? string<u8> : never", "string")]
    [InlineData("T is number ? true : false", "true")]
    [InlineData("T is number ? 42 : never", "number")]
    [InlineData("T is number ? none : never", "nil")]
    [InlineData("T is number ? Point | string : never", "Point | string")]
    [InlineData("T is number ? (T, string) : never", "{ number | string }")]
    [InlineData("T is number ? Point & fn(): void : never", "Point & (() -> nil)")]
    public void Generates_EveryShapeAnAnswerCanTake(string body, string expected)
    {
        var luau = Utility.GetLuauAST(
            $$"""
              interface Point { x: number; }
              type Answer<T> = {{body}};
              fn use(value: Answer<number>): void {
                  print(value);
              }
              """,
            true
        ).Render();

        Assert.Contains($"use(value: {expected})", luau);
    }

    /// <summary>
    ///     An answer that is an object rather than a name - what a mapped type resolves to - is written out
    ///     as the table it is, since there is no alias in the output standing for it.
    /// </summary>
    [Fact]
    public void Generates_AnObjectAnswer_AsATable()
    {
        var luau = Utility.GetLuauAST(
            """
            interface Point { x: number; }
            interface AsMut<T> { mut [K from keyof(T)]: T[K]; }
            type Mut<T> = T is unknown ? AsMut<T> : never;
            fn use(value: Mut<Point>): void {
                print(value);
            }
            """,
            true
        ).Render();

        Assert.Contains("x: number", luau);
        Assert.DoesNotContain("use(value: Mut<", luau);
    }

    /// <summary>The n-armed form emits its answer the same way the two-armed one does.</summary>
    [Fact]
    public void Generates_TheAnswerOfAMatch()
    {
        var luau = Utility.GetLuauAST(
            """
            type Answer = match true { true -> number, _ -> string };
            fn use(value: Answer): void {
                print(value);
            }
            """,
            true
        ).Render();

        Assert.Contains("type Answer = number", luau);
        Assert.Contains("use(value: Answer)", luau);
    }

    /// <summary>An answer that merges two interfaces is the table they merge into, indexer and all.</summary>
    [Fact]
    public void Generates_AMergedAnswer_AsOneTable()
    {
        var luau = Utility.GetLuauAST(
            """
            interface Point { x: number; }
            interface Keyed { [string]: number; }
            type Answer<T> = T is number ? Point & Keyed : never;
            fn use(value: Answer<number>): void {
                print(value);
            }
            """,
            true
        ).Render();

        Assert.Contains("read [string]: number", luau);
        Assert.Contains("read x: number", luau);
    }

    /// <summary>
    ///     An answer Luau has no word for anywhere inside it takes the whole thing down to <c>unknown</c>:
    ///     half a type is not a type, and a table with one member missing would be a lie about the rest.
    /// </summary>
    [Theory]
    [InlineData("keyof(T)")]
    [InlineData("keyof(T) | string")]
    [InlineData("fn(k: keyof(T)): void")]
    [InlineData("Holder<T>")]
    public void Generates_Unknown_WhenAnyPartOfTheAnswerIsStillDeferred(string member)
    {
        var luau = Utility.GetLuauAST(
            $$"""
              interface Holder<T> { [K from "a"]: {{member}}; }
              type Answer<T> = number is number ? Holder<T> : never;
              fn use<T>(value: Answer<T>): void {
                  print(value);
              }
              """,
            true
        ).Render();

        Assert.Contains("type Answer<T> = unknown", luau);
    }

    /// <summary>
    ///     An answer that is still a type parameter renders as that parameter: the conditional resolved, but
    ///     what it resolved to belongs to whatever generic encloses it.
    /// </summary>
    [Fact]
    public void Generates_AParameterAnswer_AsThatParameter()
    {
        var luau = Utility.GetLuauAST("interface Box<T> { value: (number is number ? T : never); }", true).Render();
        Assert.Contains("read value: (T)", luau);
    }

    /// <summary>
    ///     An imported alias resolves the same way one declared here does. The generator decides whether to
    ///     emit the answer or the name by reading the alias declaration's own type, and that declaration is
    ///     in another file - so this is the case where reading it could come back empty.
    /// </summary>
    [Fact]
    public void Answers_AnAliasImportedFromAnotherModule() =>
        Utility.WithTempProject(
            [
                (
                    "utils.loom",
                    """
                    export type ReturnType<T> = T is fn(..unknown[]): let R ? R : never;
                    export interface AsMut<T> { mut [K from keyof(T)]: T[K]; }
                    """
                ),
                (
                    "main.loom",
                    """
                    import type { ReturnType, AsMut } from "./utils";

                    interface Point { mut x: number; }

                    fn widen(point: AsMut<Point>): ReturnType<fn(): number> -> point.x;
                    """
                )
            ],
            (_, result) =>
            {
                Utility.AssertNoErrors(result);

                var main = Assert.Single(result.Files, file => file.SourceFile.Name == "main.loom");
                Assert.Contains("widen(point: AsMut<Point>): number", main.RenderedLuau);
            }
        );

    /// <summary>
    ///     Tier four: <c>unknown</c> rather than <c>any</c>, since <c>any</c> would silence every check
    ///     downstream of it where <c>unknown</c> makes the consumer narrow - and warned about, so what falls
    ///     through is visible rather than silent.
    /// </summary>
    [Fact]
    public void Warns_WhereItIsStillGenericAtEmission()
    {
        var diagnostics = Utility.GetGeneratorDiagnostics(
            Utilities
            + "fn identity<T>(value: ReturnType<T>): ReturnType<T> -> value;",
            true
        );

        Assert.Contains(diagnostics.Set, d => d.Code == InternalCodes.UnresolvedTypeInOutput);
    }

    /// <summary>
    ///     A generic type's own declaration is written over a parameter by definition, so there is nothing
    ///     to report there - only its uses can keep or lose the precision.
    /// </summary>
    [Fact]
    public void DoesNotWarn_AtTheDeclarationItself()
    {
        var diagnostics = Utility.GetGeneratorDiagnostics(Utilities, true);
        Assert.DoesNotContain(diagnostics.Set, d => d.Code == InternalCodes.UnresolvedTypeInOutput);
    }
}
