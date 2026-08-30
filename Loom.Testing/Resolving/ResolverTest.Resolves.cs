using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving.Symbols;
using Loom.Testing;

namespace Loom.Testing.Resolving;

public partial class ResolverTest
{
    [Fact]
    public void Resolves_ExportedDeclarations()
    {
        var model = Utility.AssertNoErrors(Utility.GetSemanticModel("export let constant = 69; export fn do_something() { }"));

        Assert.Equal(2, model.Exports.Count);
        Assert.Equal(["constant", "do_something"], model.Exports.Select(s => s.Name));

        var variable = Assert.IsType<ExportDeclaration>(model.Tree.Statements[0]).Declaration;
        Assert.Same(model.GetDeclarationSymbol(variable), model.Exports[0].Symbol);
    }

    [Fact]
    public void Resolves_InternalDeclarations_AsInternalOnlyExports()
    {
        var model = Utility.AssertNoErrors(
            Utility.GetSemanticModel("internal fn hash_key(k: number): number -> k; export fn get(key: number): number -> hash_key(key);")
        );

        Assert.Equal(2, model.Exports.Count);
        Assert.True(Assert.Single(model.FindExports("hash_key")).IsInternal);
        Assert.False(Assert.Single(model.FindExports("get")).IsInternal);

        var declaration = Assert.IsType<ExportDeclaration>(model.Tree.Statements[0]);
        Assert.True(declaration.IsInternal);
    }

    [Fact]
    public void Resolves_ExportedTypeDeclarations()
    {
        var model = Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                export type Alias = number;
                export interface Point { x: number y: number }
                export sealed interface Handle;
                export enum Direction { Up, Down }
                export trait Drawable { fn draw: void; }
                """
            )
        );

        // interfaces and enums declare a value symbol as well as a type symbol, and both are exported so
        // that an importer can use the name in either namespace
        Assert.Equal(
            [
                "Alias",
                "Point",
                "Point",
                "Handle",
                "Handle",
                "Direction",
                "Direction",
                "Drawable"
            ],
            model.Exports.Select(s => s.Name)
        );

        Assert.Equal(
            [
                SymbolKind.Type,
                SymbolKind.Variable,
                SymbolKind.Interface,
                SymbolKind.Variable,
                SymbolKind.Interface,
                SymbolKind.Variable,
                SymbolKind.EnumType,
                SymbolKind.Trait
            ],
            model.Exports.Select(s => s.Symbol.Kind)
        );

        // ...which is what FindExports hands an importing module
        Assert.Equal([SymbolKind.Variable, SymbolKind.Interface], model.FindExports("Point").Select(s => s.Symbol.Kind));
        Assert.Equal([SymbolKind.Type], model.FindExports("Alias").Select(s => s.Symbol.Kind));
        Assert.Empty(model.FindExports("Nope"));

        // none of these emit a runtime local, so none reach the module's return table
        Assert.DoesNotContain(model.Exports, s => s.EmitsRuntimeBinding);

        var trait = Assert.IsType<ExportDeclaration>(model.Tree.Statements[4]).Declaration;
        Assert.Same(model.GetDeclarationSymbol(trait), model.Exports[7].Symbol);
    }

    [Fact]
    public void Resolves_InterfaceAndTraitRelationship()
    {
        var model = Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                trait Iterator {
                    fn next(): number
                }

                interface List { }

                implement Iterator for List {
                    fn next() { return 0; }
                }
                """
            )
        );

        var trait = Assert.IsType<TraitDeclaration>(model.Tree.Statements[0]);
        var iface = Assert.IsType<InterfaceDeclaration>(model.Tree.Statements[1]);

        var traitSymbol = Assert.IsType<TraitSymbol>(model.GetDeclarationSymbol(trait, SymbolKind.Trait));

        var interfaceSymbol = Assert.IsType<InterfaceSymbol>(model.GetDeclarationSymbol(iface, SymbolKind.Interface));

        Assert.Single(interfaceSymbol.Implements);
        Assert.Same(traitSymbol, interfaceSymbol.Implements[0]);

        Assert.Single(traitSymbol.ImplementedBy);
        Assert.Same(interfaceSymbol, traitSymbol.ImplementedBy[0]);
    }

    [Fact]
    public void Resolves_InterfaceImplementationDeclaration()
    {
        var model = Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                trait Iterator {
                    fn next(): number
                }

                interface List { }

                implement Iterator for List {
                    fn next() { return 0; }
                }
                """
            )
        );

        var implement = Assert.IsType<Implement>(model.Tree.Statements[2]);

        var iface = Assert.IsType<InterfaceDeclaration>(model.Tree.Statements[1]);
        var symbol = Assert.IsType<InterfaceSymbol>(model.GetDeclarationSymbol(iface, SymbolKind.Interface));

        var implementation = Assert.Single(symbol.Implementations);

        Assert.Same(implement, implementation);
    }

    [Fact]
    public void Resolves_SelfExpression_ToImplementedInterface()
    {
        var model = Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                interface WithIndexer { [string]: number }

                trait GetValue<K, V> {
                    fn get_value(key: K): V
                }

                implement GetValue<string, number> for WithIndexer {
                    fn get_value(key) -> @[key];
                }
                """
            )
        );

        var iface = Assert.IsType<InterfaceDeclaration>(model.Tree.Statements[0]);
        var interfaceSymbol = Assert.IsType<InterfaceSymbol>(model.GetDeclarationSymbol(iface, SymbolKind.Interface));

        var implement = Assert.IsType<Implement>(model.Tree.Statements[2]);
        var method = Assert.Single(implement.Body.Implementations);
        var body = Assert.IsType<ExpressionBody>(method.Body);
        var elementAccess = Assert.IsType<ElementAccess>(body.Expression);
        var selfExpression = Assert.IsType<SelfExpression>(elementAccess.Expression);

        Assert.Same(interfaceSymbol, model.GetSymbol(selfExpression));
    }

    [Fact]
    public void Resolves_TraitTypeArgument_ReferencingUserDefinedInterface()
    {
        var model = Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                interface Bar { name: string }

                trait Serialize<T> {
                    fn serialize: T;
                }

                interface Foo { name: string }

                implement Serialize<Bar> for Foo {
                    fn serialize -> new Bar { name: "hi" }
                }
                """
            )
        );

        var bar = Assert.IsType<InterfaceDeclaration>(model.Tree.Statements[0]);
        var barSymbol = Assert.IsType<InterfaceSymbol>(model.GetDeclarationSymbol(bar, SymbolKind.Interface));

        var implement = Assert.IsType<Implement>(model.Tree.Statements[3]);
        var typeArgument = Assert.Single(implement.TraitName.TypeArguments!.ArgumentsList);
        Assert.Same(barSymbol, model.GetSymbol(typeArgument));
    }

    [Fact]
    public void ThrowsFor_SelfExpression_OutsideImplementation()
    {
        var diagnostics = Utility.GetSemanticModel("let x = @;").Diagnostics;
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.SelfOutsideImplementation,
            "'@' can only be used inside an implemented trait method or as a type predicate subject on an interface or trait member."
        );
    }

    [Fact]
    public void Resolves_SelfExpression_AsInterfaceTypePredicateSubject()
    {
        var diagnostics = Utility.GetSemanticModel("interface Container { is_kind: fn<T>(): @ is T }").Diagnostics;
        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Resolves_SelfExpression_AsTraitTypePredicateSubject()
    {
        var diagnostics = Utility.GetSemanticModel("trait HasKind<T> { fn is_kind(): @ is T; }").Diagnostics;
        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Resolves_AttributedDeclareFunctionSignature_AsFunctionSymbol_WithLuauNameAttribute()
    {
        var model = Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                [luau_name("typeof")]
                declare fn type_of(value: unknown): string;
                """
            )
        );

        var declare = Assert.IsType<Declare>(model.Tree.Statements.Single());
        var signature = Assert.IsType<DeclareFunctionSignature>(declare.Signature);
        var symbol = Assert.IsType<FunctionSymbol>(model.GetDeclarationSymbol(signature, SymbolKind.Function));

        Assert.True(symbol.TryGetIntrinsicAttribute("luau_name", out var attribute));
        Assert.Equal("luau_name", attribute.Name);
    }

    /// <summary>
    ///     Carrying attributes is not what decides a symbol's class: an unattributed signature is the same
    ///     <see cref="FunctionSymbol" /> as an attributed one, with nothing on it.
    /// </summary>
    [Fact]
    public void Resolves_NonAttributedDeclareFunctionSignature_AsFunctionSymbol_WithNoAttributes()
    {
        var model = Utility.AssertNoErrors(Utility.GetSemanticModel("declare fn print(..data: unknown[]): void;"));

        var declare = Assert.IsType<Declare>(model.Tree.Statements.Single());
        var signature = Assert.IsType<DeclareFunctionSignature>(declare.Signature);
        var symbol = Assert.IsType<FunctionSymbol>(model.GetDeclarationSymbol(signature, SymbolKind.Function));

        Assert.Empty(symbol.Attributes);
    }

    [Fact]
    public void Resolves_BareCall_ToMethodFromOtherImplementedTrait()
    {
        var model = Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                interface Container { value: number }
                trait Display { fn display: void }
                trait Balls { fn balls: void }

                implement Balls for Container {
                    fn balls -> print(@.value);
                }

                implement Display for Container {
                    fn display -> print(balls());
                }
                """
            )
        );

        var ballsImplement = Assert.IsType<Implement>(model.Tree.Statements[3]);
        var ballsMethod = Assert.Single(ballsImplement.Body.Implementations);

        var displayImplement = Assert.IsType<Implement>(model.Tree.Statements[4]);
        var displayMethod = Assert.Single(displayImplement.Body.Implementations);
        var body = Assert.IsType<ExpressionBody>(displayMethod.Body);
        var printCall = Assert.IsType<Invocation>(body.Expression);
        var ballsCall = Assert.IsType<Invocation>(Assert.Single(printCall.Arguments.ArgumentList));
        var callee = Assert.IsType<Identifier>(ballsCall.Expression);

        var symbol = model.GetSymbol(callee);
        Assert.Equal(SymbolKind.Function, symbol?.Kind);
        Assert.Same(ballsMethod, symbol?.Declaration);
    }

    [Fact]
    public void ThrowsFor_BareCall_ToMethodFromTraitImplementedLaterInFile()
    {
        var diagnostics = Utility.GetSemanticModel(
            """
            interface Container { value: number }
            trait Display { fn display: void }
            trait Balls { fn balls: void }

            implement Display for Container {
                fn display -> print(balls());
            }

            implement Balls for Container {
                fn balls -> print(@.value);
            }
            """
        ).Diagnostics;

        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindName, "Cannot find name 'balls'.");
    }

    [Fact]
    public void Resolves_PropertyPointsToInterface()
    {
        var model = Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                interface Address { }

                interface Person {
                    address: Address
                }
                """
            )
        );

        var person = Assert.IsType<InterfaceDeclaration>(model.Tree.Statements[1]);

        var symbol = Assert.IsType<InterfaceSymbol>(model.GetDeclarationSymbol(person, SymbolKind.Interface));

        var property = Assert.Single(symbol.Properties);

        Assert.NotNull(property.PointsTo);
        Assert.Equal("Address", property.PointsTo!.Name);
    }

    [Fact]
    public void Resolves_PropertyPath()
    {
        var model = Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                interface City {
                    name: string
                }

                interface Address {
                    city: City
                }

                interface Person {
                    address: Address
                }
                """
            )
        );

        var person = Assert.IsType<InterfaceDeclaration>(model.Tree.Statements[2]);

        var symbol = Assert.IsType<InterfaceSymbol>(model.GetDeclarationSymbol(person, SymbolKind.Interface));

        var property = symbol.GetPropertyAtPath(["address", "city", "name"]);

        Assert.NotNull(property);
        Assert.Equal("name", property.Name);
    }

    [Fact]
    public void Resolves_PropertyAttributes()
    {
        var model = Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                declare fn some_attribute: fn: void;
                interface Person {
                    [some_attribute]
                    name: string
                }
                """
            )
        );

        Assert.Equal(2, model.Tree.Statements.Count);
        var person = Assert.IsType<InterfaceDeclaration>(model.Tree.Statements.Last());
        var symbol = Assert.IsType<InterfaceSymbol>(model.GetDeclarationSymbol(person, SymbolKind.Interface));
        var property = Assert.Single(symbol.Properties);
        var attribute = Assert.Single(property.Attributes);
        Assert.Equal("some_attribute", attribute.Name);
    }

    [Fact]
    public void Resolves_MultipleImplementedTraits()
    {
        var model = Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                trait A { fn a(): void }
                trait B { fn b(): void }

                interface Foo { }

                implement A for Foo {
                    fn a() { }
                }

                implement B for Foo {
                    fn b() { }
                }
                """
            )
        );

        var iface = Assert.IsType<InterfaceDeclaration>(model.Tree.Statements[2]);

        var symbol = Assert.IsType<InterfaceSymbol>(model.GetDeclarationSymbol(iface, SymbolKind.Interface));

        Assert.Equal(2, symbol.Implements.Count);
        Assert.Equal(2, symbol.Implementations.Count);
    }

    [Fact]
    public void Resolves_ImplementTraitReference()
    {
        var model = Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                trait Iterator {
                    fn next(): number
                }

                interface List { }

                implement Iterator for List {
                    fn next() { return 0; }
                }
                """
            )
        );

        var implement = Assert.IsType<Implement>(model.Tree.Statements.Last());
        var symbol = model.GetSymbol(implement.TraitName);
        Assert.NotNull(symbol);
        Assert.Equal(SymbolKind.Trait, symbol.Kind);
        Assert.Equal("Iterator", symbol.Name);
    }

    [Fact]
    public void Resolves_ImplementInterfaceReference()
    {
        var model = Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                trait Iterator {
                    fn next(): number
                }

                interface List { }

                implement Iterator for List {
                    fn next() { return 0; }
                }
                """
            )
        );

        var implement = Assert.IsType<Implement>(model.Tree.Statements.Last());
        var symbol = model.GetSymbol(implement.InterfaceName);
        Assert.NotNull(symbol);
        Assert.Equal(SymbolKind.Interface, symbol.Kind);
        Assert.Equal("List", symbol.Name);
    }

    [Fact]
    public void Resolves_TraitTypeParameter()
    {
        var model = Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                trait Iterator<T> {
                    fn next(): T
                }
                """
            )
        );

        var trait = Assert.IsType<TraitDeclaration>(model.Tree.Statements.Single());
        var member = Assert.Single(trait.Body.Members);
        var returnType = Assert.IsType<TypeName>(member.ReturnType.Type);
        var symbol = model.GetSymbol(returnType);

        Assert.NotNull(symbol);
        Assert.Equal(SymbolKind.Type, symbol.Kind);
        Assert.Equal("T", symbol.Name);
    }

    [Fact]
    public void Resolves_TraitTypeReference()
    {
        var model = Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                trait Iterator {
                    fn next(): number
                }

                mut x: Iterator
                """
            )
        );

        var declaration = Assert.IsType<VariableDeclaration>(model.Tree.Statements.Last());
        var typeName = Assert.IsType<TypeName>(declaration.ColonTypeClause!.Type);
        var symbol = model.GetSymbol(typeName);
        Assert.NotNull(symbol);
        Assert.Equal("Iterator", symbol.Name);
        Assert.Equal(SymbolKind.Trait, symbol.Kind);
    }

    [Fact]
    public void Resolves_TypeParameter_InFunctionSignature()
    {
        var model = Utility.AssertNoErrors(Utility.GetSemanticModel("fn identity<T>(x: T): T { return x; }"));
        var fn = Assert.IsType<FunctionDeclaration>(model.Tree.Statements.Single());
        var returnType = fn.ReturnType!.Type as TypeName;
        Assert.NotNull(returnType);
        Assert.Equal("T", returnType.Name.Text);

        var symbol = model.GetSymbol(returnType);
        Assert.NotNull(symbol);
        Assert.Equal(SymbolKind.Type, symbol.Kind);
        Assert.Equal("T", symbol.Name);
    }

    [Fact]
    public void Resolves_TypeParameter_InTypeAlias()
    {
        var model = Utility.AssertNoErrors(Utility.GetSemanticModel("type Container<T> = T"));
        var alias = Assert.IsType<TypeAlias>(model.Tree.Statements.Single());
        var typeName = Assert.IsType<TypeName>(alias.EqualsTypeClause.Type);
        var symbol = model.GetSymbol(typeName);
        Assert.NotNull(symbol);
        Assert.Equal("T", symbol.Name);
        Assert.Equal(SymbolKind.Type, symbol.Kind);
    }

    [Fact]
    public void Resolves_TypeParameter_Shadowing()
    {
        var model = Utility.AssertNoErrors(Utility.GetSemanticModel("type Outer<T> = fn<T>(x: T): T"));
        var alias = Assert.IsType<TypeAlias>(model.Tree.Statements.Single());
        var fnType = Assert.IsType<FunctionType>(alias.EqualsTypeClause.Type);
        var paramType = fnType.Parameters!.ParameterList[0].ColonTypeClause!.Type as TypeName;
        Assert.NotNull(paramType);

        var symbol = model.GetSymbol(paramType);
        Assert.NotNull(symbol);
        Assert.Equal("T", symbol.Name);
        Assert.Equal(fnType.TypeParameters!.ParameterList[0], symbol.Declaration);
    }

    [Fact]
    public void Resolves_Interface_WithGenericConstraint()
    {
        var model = Utility.AssertNoErrors(Utility.GetSemanticModel("type Foo = number; interface I<T: Foo> { }"));
        Assert.Equal(2, model.Tree.Statements.Count);

        var iface = Assert.IsType<InterfaceDeclaration>(model.Tree.Statements.Last());
        Assert.NotNull(iface.TypeParameters);

        var tp = iface.TypeParameters.ParameterList[0];
        Assert.NotNull(tp.ColonTypeClause);

        var constraint = tp.ColonTypeClause.Type;
        var symbol = model.GetSymbol(constraint);
        Assert.NotNull(symbol);
        Assert.Equal("Foo", symbol.Name);
    }

    [Fact]
    public void Resolves_TypeParameter_InTypeArgument()
    {
        var model = Utility.AssertNoErrors(Utility.GetSemanticModel("type Foo<T> = T; fn foo<T>(x: Foo<T>) { }"));
        Assert.Equal(2, model.Tree.Statements.Count);

        var fn = Assert.IsType<FunctionDeclaration>(model.Tree.Statements.Last());
        Assert.NotNull(fn.Parameters);

        var param = fn.Parameters.ParameterList[0];
        var typeName = Assert.IsType<TypeName>(param.ColonTypeClause!.Type);
        Assert.NotNull(typeName.TypeArguments);

        var arg = typeName.TypeArguments.ArgumentsList[0] as TypeName;
        Assert.NotNull(arg);

        var symbol = model.GetSymbol(arg);
        Assert.NotNull(symbol);
        Assert.Equal("T", symbol.Name);
    }

    [Fact]
    public void Resolves_IntrinsicTypeSymbols()
    {
        var model = Utility.AssertNoErrors(Utility.GetSemanticModel("mut x: Range;"));
        Assert.Single(model.Tree.Statements);

        var declaration = Assert.IsType<VariableDeclaration>(model.Tree.Statements.First());
        Assert.NotNull(declaration.ColonTypeClause);

        var symbol = model.GetSymbol(declaration.ColonTypeClause.Type);
        Assert.NotNull(symbol);
        Assert.True(symbol.IsGlobal);
        Assert.True(symbol.IsIntrinsic);
    }

    [Fact]
    public void Resolves_GlobalSymbols_FromCompilationUnit()
    {
        var model = Utility.AssertNoErrors(Utility.GetSemanticModel("print(42);"));
        Assert.Single(model.Tree.Statements);

        var stmt = Assert.IsType<ExpressionStatement>(model.Tree.Statements.First());
        var invoc = Assert.IsType<Invocation>(stmt.Expression);
        var ident = Assert.IsType<Identifier>(invoc.Expression);
        var symbol = model.GetSymbol(ident);
        Assert.NotNull(symbol);
        Assert.Equal("print", symbol.Name);
        Assert.True(symbol.IsGlobal);
        Assert.True(symbol.IsIntrinsic);
    }

    [Fact]
    public void Resolves_Declare_InsideBlock()
    {
        var model = Utility.AssertNoErrors(Utility.GetSemanticModel("if true { declare let x: number; }"));
        var ifStmt = Assert.IsType<If>(model.Tree.Statements.Single());
        var block = Assert.IsType<Block>(ifStmt.ThenBranch);
        var declare = Assert.IsType<Declare>(block.Statements.Single());
        var sig = Assert.IsType<DeclareVariableSignature>(declare.Signature);
        var symbol = model.GetDeclarationSymbol(sig);
        Assert.NotNull(symbol);
        Assert.Equal("x", symbol.Name);
    }

    [Fact]
    public void Resolves_FunctionName_InsideOwnBody()
    {
        var model = Utility.AssertNoErrors(
            Utility.GetSemanticModel("fn factorial(n: number): number { if n <= 1 { return 1 } else { return n * factorial(n - 1) } }")
        );

        var fn = Assert.IsType<FunctionDeclaration>(model.Tree.Statements.Single());
        var block = Assert.IsType<Block>(fn.Body);
        var ifStmt = Assert.IsType<If>(block.Statements.First());
        Assert.NotNull(ifStmt.ElseBranch);

        var elseBlock = Assert.IsType<Block>(ifStmt.ElseBranch!.Branch);
        var ret = Assert.IsType<Return>(elseBlock.Statements.First());
        var binary = Assert.IsType<BinaryOperator>(ret.Expression!);
        var invocation = Assert.IsType<Invocation>(binary.Right);
        var ident = Assert.IsType<Identifier>(invocation.Expression);
        var symbol = model.GetSymbol(ident);
        Assert.NotNull(symbol);
        Assert.Equal("factorial", symbol.Name);
    }

    [Fact]
    public void Resolves_MatchPatternBinding_InBody()
    {
        var model = Utility.AssertNoErrors(Utility.GetSemanticModel("match 1 { x -> x }"));
        var match = Assert.IsType<MatchExpression>(Assert.IsType<ExpressionStatement>(model.Tree.Statements.Single()).Expression);
        var arm = Assert.Single(match.Arms);
        var pattern = Assert.IsType<IdentifierPattern>(arm.Pattern);
        var declaration = model.GetDeclarationSymbol(pattern);
        Assert.NotNull(declaration);
        Assert.Equal("x", declaration.Name);
        Assert.Equal(SymbolKind.Variable, declaration.Kind);

        var body = Assert.IsType<Identifier>(arm.Body);
        Assert.Equal(declaration, model.GetSymbol(body));
    }

    [Fact]
    public void Resolves_MatchPatternBinding_InGuard()
    {
        var model = Utility.AssertNoErrors(Utility.GetSemanticModel("match 1 { n when n > 0 -> n }"));
        var match = Assert.IsType<MatchExpression>(Assert.IsType<ExpressionStatement>(model.Tree.Statements.Single()).Expression);
        var arm = Assert.Single(match.Arms);
        var pattern = Assert.IsType<IdentifierPattern>(arm.Pattern);
        var declaration = model.GetDeclarationSymbol(pattern);
        Assert.NotNull(declaration);

        var guard = Assert.IsType<BinaryOperator>(arm.Guard);
        var guardIdent = Assert.IsType<Identifier>(guard.Left);
        Assert.Equal(declaration, model.GetSymbol(guardIdent));
    }

    [Fact]
    public void Resolves_MatchTypedPattern_TypeName()
    {
        var model = Utility.AssertNoErrors(Utility.GetSemanticModel("interface Foo {}; match 1 { s when Foo -> s }"));
        var match = Assert.IsType<MatchExpression>(Assert.IsType<ExpressionStatement>(model.Tree.Statements[1]).Expression);
        var arm = Assert.Single(match.Arms);
        var typed = Assert.IsType<TypedPattern>(arm.Pattern);
        var typeName = Assert.IsType<TypeName>(typed.Type);
        var symbol = model.GetSymbol(typeName);
        Assert.NotNull(symbol);
        Assert.Equal("Foo", symbol.Name);
    }

    [Fact]
    public void Resolves_MatchTypePattern_ObjectFieldBinding()
    {
        var model = Utility.AssertNoErrors(Utility.GetSemanticModel("interface Foo { field: number }; match 1 { Foo { field } -> field }"));
        var match = Assert.IsType<MatchExpression>(Assert.IsType<ExpressionStatement>(model.Tree.Statements[1]).Expression);
        var arm = Assert.Single(match.Arms);
        var typePattern = Assert.IsType<TypePattern>(arm.Pattern);
        var field = Assert.Single(typePattern.ObjectPattern!.Fields);
        var identifierPattern = Assert.IsType<IdentifierPattern>(field.Pattern);
        var declaration = model.GetDeclarationSymbol(identifierPattern);
        Assert.NotNull(declaration);
        Assert.Equal("field", declaration.Name);
        Assert.Equal(declaration, model.GetSymbol(Assert.IsType<Identifier>(arm.Body)));
    }

    [Fact]
    public void Resolves_MatchArrayRestBinding()
    {
        var model = Utility.AssertNoErrors(Utility.GetSemanticModel("match 1 { [head, ..rest] -> rest }"));
        var match = Assert.IsType<MatchExpression>(Assert.IsType<ExpressionStatement>(model.Tree.Statements.Single()).Expression);
        var arm = Assert.Single(match.Arms);
        var array = Assert.IsType<ArrayPattern>(arm.Pattern);
        Assert.NotNull(array.Rest);
        var restPattern = Assert.IsType<IdentifierPattern>(array.Rest.Pattern);
        var declaration = model.GetDeclarationSymbol(restPattern);
        Assert.NotNull(declaration);
        Assert.Equal("rest", declaration.Name);
        Assert.Equal(declaration, model.GetSymbol(Assert.IsType<Identifier>(arm.Body)));
    }
}
