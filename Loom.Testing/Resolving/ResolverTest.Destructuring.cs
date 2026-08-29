using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving.Symbols;

namespace Loom.Testing;

public partial class ResolverTest
{
    [Fact]
    public void Resolves_ArrayDestructuring_DeclaresAllBindings() =>
        Utility.AssertNoErrors(Utility.GetSemanticModel("let array = [1, 2]; let [first, second] = array; print(first); print(second);").Diagnostics);

    [Fact]
    public void Resolves_ObjectDestructuring_DeclaresAllBindings() =>
        Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                "interface User { name: string } let user = new User { name: \"Ada\" }; let { name } = user; print(name);"
            ).Diagnostics
        );

    [Fact]
    public void Resolves_ObjectDestructuring_WithAlias_DeclaresBindingUnderAliasName()
    {
        var diagnostics = Utility.GetSemanticModel(
            "interface User { age: number } let user = new User { age: 30 }; let { age: userAge } = user; print(userAge);"
        ).Diagnostics;

        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void ThrowsFor_ObjectDestructuring_AliasName_IsNotVisibleUnderOriginalName()
    {
        var diagnostics = Utility.GetSemanticModel(
            "interface User { age: number } let user = new User { age: 30 }; let { age: userAge } = user; print(age);"
        ).Diagnostics;

        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindName, "Cannot find name 'age'.");
    }

    [Fact]
    public void ThrowsFor_Destructuring_MissingInitializer()
    {
        var diagnostics = Utility.GetSemanticModel("let [first, second];").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.MustHaveInitializer, "Destructuring declarations must be initialized.");
    }

    [Fact]
    public void ThrowsFor_Destructuring_WithMutKeyword()
    {
        var diagnostics = Utility.GetSemanticModel("let array = [1, 2]; mut [first, second] = array;").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidDestructureTarget, "Destructuring declarations must use 'let', not 'mut'.");
    }

    [Fact]
    public void ThrowsFor_Destructuring_DuplicateBindingName()
    {
        var diagnostics = Utility.GetSemanticModel("let array = [1, 2]; let [x, x] = array;").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.DuplicateName, "Variable 'x' is already declared in this scope.");
    }

    [Fact]
    public void Resolves_TupleDestructuring_DeclaresAllBindings() =>
        Utility.AssertNoErrors(
            Utility.GetSemanticModel("let t: (string, number) = (\"abc\", 420); let (one, two) = t; print(one); print(two);").Diagnostics
        );

    [Fact]
    public void Resolves_TupleConstraint_ResolvesTupleName() =>
        Utility.AssertNoErrors(Utility.GetSemanticModel("declare fn something<T: Tuple>(..args: T): void;").Diagnostics);

    [Fact]
    public void Resolves_NestedObjectDestructuring_DeclaresOnlyTheLeafBinding()
    {
        const string source = """
            interface Address { city: string }
            interface User { name: string, address: Address }
            let user = new User { name: "a", address: new Address { city: "b" } };
            let { address: { city } } = user;
            print(city);
            """;

        Utility.AssertNoErrors(Utility.GetSemanticModel(source).Diagnostics);
    }

    [Fact]
    public void ThrowsFor_NestedObjectDestructuring_AddressIsNotItselfDeclared()
    {
        const string source = """
            interface Address { city: string }
            interface User { name: string, address: Address }
            let user = new User { name: "a", address: new Address { city: "b" } };
            let { address: { city } } = user;
            print(address);
            """;

        Utility.AssertDiagnostic(Utility.GetSemanticModel(source).Diagnostics, InternalCodes.CannotFindName, "Cannot find name 'address'.");
    }

    [Fact]
    public void Resolves_ArrayNestedInsideObjectDestructuring_DeclaresAllBindings()
    {
        const string source = """
            interface Summary { scores: number[] }
            let summary = new Summary { scores: [10, 20] };
            let { scores: [first, second] } = summary;
            print(first); print(second);
            """;

        Utility.AssertNoErrors(Utility.GetSemanticModel(source).Diagnostics);
    }

    [Fact]
    public void Resolves_ObjectNestedInsideArrayDestructuring_DeclaresAllBindings()
    {
        const string source = """
            interface Point { x: number, y: number }
            let points = [new Point { x: 1, y: 2 }];
            let [{ x: firstX }] = points;
            print(firstX);
            """;

        Utility.AssertNoErrors(Utility.GetSemanticModel(source).Diagnostics);
    }

    [Fact]
    public void Resolves_ArrayDestructuringElement_WithDefault()
    {
        const string source = """
            let maybe_pair = [1];
            let [first, second = 0] = maybe_pair;
            print(first); print(second);
            """;

        Utility.AssertNoErrors(Utility.GetSemanticModel(source).Diagnostics);
    }

    [Fact]
    public void Resolves_ObjectDestructuringField_WithDefault()
    {
        const string source = """
            interface Config { retries: number? }
            let config = new Config { retries: none };
            let { retries = 3 } = config;
            print(retries);
            """;

        Utility.AssertNoErrors(Utility.GetSemanticModel(source).Diagnostics);
    }

    [Fact]
    public void ThrowsFor_DestructuringDefault_ReferencingUndeclaredName()
    {
        const string source = """
            let maybe_pair = [1];
            let [first, second = missing] = maybe_pair;
            """;

        Utility.AssertDiagnostic(Utility.GetSemanticModel(source).Diagnostics, InternalCodes.CannotFindName, "Cannot find name 'missing'.");
    }
}
