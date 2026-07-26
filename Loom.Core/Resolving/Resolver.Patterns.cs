using Loom.Core.Parsing.AST;

namespace Loom.Core.Resolving;

public sealed partial class Resolver
{
    public override bool VisitIdentifierPattern(IdentifierPattern identifierPattern) =>
        DeclareVariable(identifierPattern, identifierPattern.Name.Text, SymbolKind.Variable);

    public override bool VisitLetPattern(LetPattern letPattern) => DeclareVariable(letPattern, letPattern.Name.Text, SymbolKind.Variable);

    public override bool VisitTypedPattern(TypedPattern typedPattern) =>
        DeclareVariable(typedPattern, typedPattern.Name.Text, SymbolKind.Variable)
        && Visit(typedPattern.Type)
        && (typedPattern.ObjectPattern == null || Visit(typedPattern.ObjectPattern));

    public override bool VisitTypePattern(TypePattern typePattern) =>
        Visit(typePattern.Type)
        && (typePattern.ObjectPattern == null || Visit(typePattern.ObjectPattern));

    public override bool VisitObjectPattern(ObjectPattern objectPattern)
    {
        foreach (var field in objectPattern.Fields)
            if (!Visit(field))
                return false;

        return true;
    }

    public override bool VisitObjectPatternField(ObjectPatternField objectPatternField) => Visit(objectPatternField.Pattern);

    public override bool VisitArrayPattern(ArrayPattern arrayPattern)
    {
        foreach (var element in arrayPattern.Elements)
            if (!Visit(element))
                return false;

        return arrayPattern.Rest == null || Visit(arrayPattern.Rest);
    }

    public override bool VisitRestPattern(RestPattern restPattern) => Visit(restPattern.Pattern);

    public override bool VisitOrPattern(OrPattern orPattern)
    {
        foreach (var pattern in orPattern.Patterns)
            if (!Visit(pattern))
                return false;

        return true;
    }

    public override bool VisitWildcardPattern(WildcardPattern wildcardPattern) => true;

    public override bool VisitLiteralPattern(LiteralPattern literalPattern) => true;

    public override bool VisitRangePattern(RangePattern rangePattern) => Visit(rangePattern.Minimum) && Visit(rangePattern.Maximum);

    public override bool VisitNullPattern(NullPattern nullPattern) => true;
}
