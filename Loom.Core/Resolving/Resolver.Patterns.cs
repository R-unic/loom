using Loom.Core.Parsing.AST;

namespace Loom.Core.Resolving;

public sealed partial class Resolver
{
    public override bool VisitIdentifierPattern(IdentifierPattern identifierPattern) =>
        DeclareVariable(identifierPattern, identifierPattern.Name.Text);

    public override bool VisitLetPattern(LetPattern letPattern) => DeclareVariable(letPattern, letPattern.Name.Text);

    public override bool VisitTypedPattern(TypedPattern typedPattern) =>
        DeclareVariable(typedPattern, typedPattern.Name.Text)
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

    public override bool VisitTuplePattern(TuplePattern tuplePattern)
    {
        foreach (var pattern in tuplePattern.Patterns)
            if (!Visit(pattern))
                return false;

        return true;
    }

    // Each alternative resolves in its own throwaway scope so alternatives binding the same name (e.g.
    // `Foo { x } | Bar { x } -> x`) don't collide as duplicates - only one arm ever runs. The first
    // alternative's bindings are then declared for real in the enclosing scope, for the arm body to see.
    public override bool VisitOrPattern(OrPattern orPattern)
    {
        ResolverScope? firstScope = null;
        foreach (var pattern in orPattern.Patterns)
        {
            var scope = PushScope();
            var success = Visit(pattern);
            PopScope();
            if (!success)
                return false;

            firstScope ??= scope;
        }

        return firstScope == null
            || firstScope.Lookup(SymbolNamespace.Value).Values.SelectMany(symbols => symbols).All(symbol => DeclareVariable(symbol.Declaration, symbol));
    }

    public override bool VisitAndPattern(AndPattern andPattern) => Visit(andPattern.Pattern) && Visit(andPattern.Guard);

    public override bool VisitWildcardPattern(WildcardPattern wildcardPattern) => true;

    public override bool VisitLiteralPattern(LiteralPattern literalPattern) => true;

    public override bool VisitRangePattern(RangePattern rangePattern) => Visit(rangePattern.Minimum) && Visit(rangePattern.Maximum);

    public override bool VisitNullPattern(NullPattern nullPattern) => true;
}
