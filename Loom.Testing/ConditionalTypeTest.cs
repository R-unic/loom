using Loom.Core.Diagnostics;
using Loom.Core.TypeChecking;
using Loom.Core.TypeChecking.Types;
using AstConditionalType = Loom.Core.Parsing.AST.ConditionalType;
using InferType = Loom.Core.Parsing.AST.InferType;
using TypeMatch = Loom.Core.Parsing.AST.TypeMatch;
using TypePredicateType = Loom.Core.Parsing.AST.TypePredicateType;
using WildcardType = Loom.Core.Parsing.AST.WildcardType;

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
        Utility.AssertDiagnostic(diagnostics, InternalCodes.EmptyMatch, "A type-level 'match' must have at least one arm.");
    }

    /// <summary>
    ///     <c>let</c> and <c>_</c> only mean anything inside a pattern; outside one they are the ordinary
    ///     keyword and an ordinary name.
    /// </summary>
    [Fact]
    public void Reports_ABinderOutsideAPattern()
    {
        Assert.NotEmpty(Utility.GetParserDiagnostics("type X = let R;").Set);
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
    public void Allows_TwoArmsToReuseABinderName()
    {
        Utility.AssertNoErrors(Utility.GetAnalysisDiagnostics("type X<T> = match T { (let E)[] -> E, Future<let E> -> E, _ -> never };"));
    }

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
        const string source = """
            type IsNumber<T> = T is number ? true : false;
            """;

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
        const string source = """
            type NumericElement<T> = T is (let E: number)[] ? E : never;
            """;

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
    ///     Tier four: <c>unknown</c> rather than <c>any</c>, since <c>any</c> would silence every check
    ///     downstream of it where <c>unknown</c> makes the consumer narrow - and warned about, so what falls
    ///     through is visible rather than silent.
    /// </summary>
    [Fact]
    public void Warns_WhereItIsStillGenericAtEmission()
    {
        var diagnostics = Utility.GetGeneratorDiagnostics(
            Utilities
            + """
              fn identity<T>(value: ReturnType<T>): ReturnType<T> -> value;
              """,
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
