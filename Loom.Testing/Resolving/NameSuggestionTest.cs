using Loom.Core.Diagnostics;

namespace Loom.Testing.Resolving;

[Collection("Assembly")]
public class NameSuggestionTest
{
    [Fact]
    public void Suggests_ClosestNameInScope_ForUnresolvedValue()
    {
        var diagnostics = Utility.GetSemanticModel("let price = 5;\nlet cost = pric * 2;").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindName, "Cannot find name 'pric'.", "did you mean 'price'?");
    }

    [Fact]
    public void Suggests_ClosestNameInScope_ForUnresolvedType()
    {
        var diagnostics = Utility.GetSemanticModel("interface Vector3 { x: number }\nfn f(): Vecotr3 { }").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindName, "Cannot find type 'Vecotr3'.", "did you mean 'Vector3'?");
    }

    [Fact]
    public void SuggestsNothing_WhenNoNameIsCloseEnough()
    {
        var diagnostics = Utility.GetSemanticModel("let price = 5;\nlet cost = zzzzzzzzzzzz;").Diagnostics;
        var diagnostic = diagnostics.Find(d => d.Code == InternalCodes.CannotFindName);

        Assert.NotNull(diagnostic);
        Assert.Null(diagnostic.Hint);
    }

    [Fact]
    public void DoesNotSuggest_TypeOnlyName_InValuePosition()
    {
        var diagnostics = Utility.GetSemanticModel("type MyType = number;\nlet result = MyTyp;").Diagnostics;
        var diagnostic = diagnostics.Find(d => d.Code == InternalCodes.CannotFindName);

        Assert.NotNull(diagnostic);
        Assert.Null(diagnostic.Hint);
    }

    [Fact]
    public void DoesNotSuggest_ValueOnlyName_InTypePosition()
    {
        var diagnostics = Utility.GetSemanticModel("let myValue = 1;\nfn f(): MyValu { }").Diagnostics;
        var diagnostic = diagnostics.Find(d => d.Code == InternalCodes.CannotFindName);

        Assert.NotNull(diagnostic);
        Assert.Null(diagnostic.Hint);
    }

    [Fact]
    public void DoesNotSuggest_NameOutOfScope()
    {
        var diagnostics = Utility.GetSemanticModel("fn f() { let localOne = 1; }\nfn g() { print(localOn); }").Diagnostics;
        var diagnostic = diagnostics.Find(d => d.Code == InternalCodes.CannotFindName);

        Assert.NotNull(diagnostic);
        Assert.DoesNotContain("localOne", diagnostic.Hint ?? "");
    }
}
