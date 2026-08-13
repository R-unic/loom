using Loom.Core.Diagnostics;
using Loom.Core.TypeChecking;
using Loom.Core.TypeChecking.Types;
using AstIndexedType = Loom.Core.Parsing.AST.IndexedType;
using IndexerDeclaration = Loom.Core.Parsing.AST.IndexerDeclaration;
using KeyOf = Loom.Core.Parsing.AST.KeyOf;
using MappedTypeDeclaration = Loom.Core.Parsing.AST.MappedTypeDeclaration;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.Testing;

/// <summary>
///     <c>interface AsMut&lt;T&gt; { mut [K from keyof(T)]: T[K] }</c> - one member per key of another
///     type. Issue #75.
/// </summary>
[Collection("Assembly")]
public class MappedTypeTest
{
    private const string Point = """
        interface Point {
            x: number;
            y: string;
        }


        """;

    [Fact]
    public void Parses_TheBinderAndItsKeySource()
    {
        var tree = Utility.GetAST("interface AsMut<T> { mut [K from keyof(T)]: T[K]; }");
        var mapped = Assert.Single(tree.GetDescendants<MappedTypeDeclaration>());

        Assert.Equal("K", mapped.Name.Text);
        Assert.NotNull(mapped.MutKeyword);
        Assert.IsType<KeyOf>(mapped.SourceType);
        Assert.IsType<AstIndexedType>(mapped.ColonTypeClause.Type);
    }

    /// <summary>An indexer keyed on a type rather than on a bound name is still an indexer.</summary>
    [Fact]
    public void Parses_APlainIndexer_AsAnIndexer()
    {
        var tree = Utility.GetAST("interface Bag<K, V> { [K]: V; }");

        Assert.Empty(tree.GetDescendants<MappedTypeDeclaration>());
        Assert.Single(tree.GetDescendants<IndexerDeclaration>());
    }

    [Fact]
    public void Reports_AMemberDeclaredBesideTheMapping()
    {
        var diagnostics = Utility.GetAnalysisDiagnostics("interface Bad<T> { [K from keyof(T)]: T[K]; extra: number; }");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.InvalidMappedType,
            "Type 'Bad' maps over another type's keys, so it may not declare members of its own."
        );
    }

    [Fact]
    public void Reports_AMappedTypeThatAlsoInherits()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(Point + "interface Bad<T>: Point { [K from keyof(T)]: T[K]; }");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.InvalidMappedType,
            "Type 'Bad' maps over another type's keys, so it cannot also inherit members."
        );
    }

    /// <summary>
    ///     Concrete properties rather than one indexer over the whole key union: which key has which type is
    ///     the whole of what <c>T[K]</c> says, and an indexer keyed on <c>"x" | "y"</c> would answer
    ///     <c>number | string</c> for both of them.
    /// </summary>
    [Fact]
    public void Expands_ToOnePropertyPerKey_WithItsOwnType()
    {
        var objectType = MappedProperties("mut [K from keyof(T)]: T[K];");

        Assert.Null(objectType.Indexer);
        Assert.Equal(["x", "y"], objectType.Properties.ConvertAll(p => p.Name));
        Assert.Equal("number", objectType.Properties[0].ValueType.ToString());
        Assert.Equal("string", objectType.Properties[1].ValueType.ToString());
    }

    [Fact]
    public void Carries_TheMutModifierOntoEveryMappedMember()
    {
        Assert.All(MappedProperties("mut [K from keyof(T)]: T[K];").Properties, property => Assert.True(property.IsMutable));
        Assert.All(MappedProperties("[K from keyof(T)]: T[K];").Properties, property => Assert.False(property.IsMutable));
    }

    private static ObjectType MappedProperties(string member)
    {
        var type = Utility.GetLastStatementType(
            $$"""
              {{Point}}
              interface Mapped<T> { {{member}} }
              declare let point: Mapped<Point>;
              point
              """
        );

        return Assert.IsType<ObjectType>(TypeSimplifier.Expanded(type));
    }

    /// <summary>
    ///     Mapping over a computed key set, which is what <c>Omit</c> is: the source is an instantiation of
    ///     a conditional alias, and the keys only exist once that has been answered.
    /// </summary>
    [Fact]
    public void Maps_OverKeysAConditionalTypeProduced()
    {
        var type = Utility.GetLastStatementType(
            Point
            + """
              type Exclude<T, U> = match each T {
                  U -> never,
                  let Other -> Other,
              };

              interface Omit<T, K> { [P from Exclude<keyof(T), K>]: T[P]; }
              declare let trimmed: Omit<Point, "y">;
              trimmed
              """
        );

        var objectType = Assert.IsType<ObjectType>(TypeSimplifier.Expanded(type));
        Assert.Equal(["x"], objectType.Properties.ConvertAll(p => p.Name));
    }

    /// <summary>Nothing to expand while the keys are a parameter, so it stays a <see cref="MappedType" />.</summary>
    [Fact]
    public void Defers_WhileTheKeysAreStillAParameter()
    {
        var type = Utility.GetLastStatementType(
            """
            interface AsMut<T> { mut [K from keyof(T)]: T[K]; }
            declare fn identity<T>(value: AsMut<T>): AsMut<T>;
            identity
            """
        );

        var function = Assert.IsType<FunctionType>(type);
        Assert.IsType<MappedType>(TypeSimplifier.Expanded(function.ReturnType));
    }

    [Fact]
    public void ReadsTheMappedMembers_AtTheirOwnTypes()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            Point
            + """
              interface AsMut<T> { mut [K from keyof(T)]: T[K]; }
              declare let point: AsMut<Point>;
              let width: number = point.x;
              let name: string = point.y;
              """
        );

        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Reports_AMappedMemberUsedAtTheWrongType()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            Point
            + """
              interface AsMut<T> { mut [K from keyof(T)]: T[K]; }
              declare let point: AsMut<Point>;
              let width: string = point.x;
              """
        );

        Assert.Contains(diagnostics.Set, d => d.Code == InternalCodes.TypeMismatch);
    }

    [Fact]
    public void MappedType_IsAssignableTo_ItsExpandedForm()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            Point
            + """
              interface AsMut<T> { mut [K from keyof(T)]: T[K]; }
              declare let point: AsMut<Point>;
              let plain: Point = point;
              """
        );

        Utility.AssertNoErrors(diagnostics);
    }

    /// <summary>
    ///     Luau has no mapped types, but it does have <c>keyof</c> and <c>index</c>, so the deferred form
    ///     lowers to an indexer over the whole key set - the shape issue #75 asks for.
    /// </summary>
    [Fact]
    public void Generates_AnIndexerOverTheKeySet()
    {
        var luau = Utility.GetLuauAST("interface AsMut<T> { mut [K from keyof(T)]: T[K]; }", true).Render();
        Assert.Contains("type AsMut<T> = { [keyof<T>]: index<T, keyof<T>> }", luau);
    }

    [Fact]
    public void Generates_AReadOnlyIndexer_WithoutMut()
    {
        var luau = Utility.GetLuauAST("interface AsRead<T> { [K from keyof(T)]: T[K]; }", true).Render();
        Assert.Contains("type AsRead<T> = { read [keyof<T>]: index<T, keyof<T>> }", luau);
    }

    /// <summary>
    ///     A use keeps the alias's name rather than being expanded inline: unlike a conditional type, Luau
    ///     can express this one, so writing the answer out at every use would only cost the name.
    /// </summary>
    [Fact]
    public void Generates_AUseAsTheAliasName()
    {
        var luau = Utility.GetLuauAST(
            Point
            + """
              interface AsMut<T> { mut [K from keyof(T)]: T[K]; }
              declare let point: AsMut<Point>;
              let alias: AsMut<Point> = point;
              """,
            true
        ).Render();

        Assert.Contains("AsMut<Point>", luau);
    }

    [Fact]
    public void MappedType_ToString()
    {
        var parameter = new TypeParameter("T");
        var binder = new TypeParameter("K");
        var mapped = new MappedType(binder, new KeyOfType(parameter), new IndexedType(parameter, binder), true);

        Assert.Equal("{ mut [K from keyof(T)]: T[K] }", mapped.ToString());
    }

    [Fact]
    public void MappedType_Equals_IgnoresTheBindersName()
    {
        var source = new KeyOfType(new TypeParameter("T"));
        Type first = new MappedType(new TypeParameter("K"), source, PrimitiveType.Number, false);
        Type second = new MappedType(new TypeParameter("P"), source, PrimitiveType.Number, false);
        Type mutable = new MappedType(new TypeParameter("K"), source, PrimitiveType.Number, true);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, mutable);
    }
}
