using Loom.Core.Parsing.AST;
using Loom.Luau;
using Loom.Luau.AST;
using TupleType = Loom.Core.TypeChecking.Types.TupleType;

namespace Loom.Core.Generation;

public sealed partial class LuauGenerator
{
    public override LuauNode VisitDestructuringDeclaration(DestructuringDeclaration destructuringDeclaration)
    {
        var initializerExpression = destructuringDeclaration.EqualsValueClause!.Value;
        if (destructuringDeclaration.Target is TupleDestructuringTarget tupleTarget)
            return EmitTupleDestructuring(tupleTarget, initializerExpression);

        var initializer = Visit(initializerExpression);
        var subject = _state.PushToVariable("_destructure", initializer);
        EmitDestructuringTarget(destructuringDeclaration.Target, subject);

        return new NoOpStatement();
    }

    /// <summary>
    ///     Emits every leaf binding under <paramref name="target" />, reading each one off <paramref name="subject" />
    ///     - the value this target destructures. A field or element that renames into a nested pattern instead
    ///     of a plain name contributes no local of its own here: <see cref="PropertyAccess" />/<see cref="Luau.AST.ElementAccess" />
    ///     already nest (<c>user.address.city</c> is just a <c>PropertyAccess</c> whose own target is another
    ///     <c>PropertyAccess</c>), so recursing with the deeper access expression as the new subject is enough to
    ///     flatten arbitrarily nested patterns into one local per leaf, exactly as a top-level one already was.
    /// </summary>
    private void EmitDestructuringTarget(DestructuringTarget target, LuauExpression subject)
    {
        switch (target)
        {
            case ArrayDestructuringTarget arrayTarget:
                for (var i = 0; i < arrayTarget.Elements.Count; i++)
                    EmitDestructuringElement(arrayTarget.Elements[i], new Luau.AST.ElementAccess(subject, new NumberLiteral(i + 1)));

                break;

            case ObjectDestructuringTarget objectTarget:
                foreach (var field in objectTarget.Fields)
                    EmitObjectDestructuringField(field, new Luau.AST.PropertyAccess(subject, [field.Name.Text]));

                break;

            case TupleDestructuringTarget tupleTarget:
                // Only reachable nested inside an object/array field - a top-level tuple target goes through
                // EmitTupleDestructuring instead, which can special-case a literal tuple or a multi-return call.
                // Nested, the value is always already sitting in a table read off the outer subject, so it is
                // always table.unpack, never those two faster paths.
                var names = tupleTarget.Elements.ConvertAll(e => e.Name?.Text ?? "_");
                foreach (var name in names)
                    _state.Scope.AddIdentifier(name);

                _state.Prereq(new MultiConstVariable(names, LuauFactory.TableCall("unpack", [subject])));
                break;
        }
    }

    private void EmitDestructuringElement(DestructuringElement element, LuauExpression access)
    {
        if (element.NestedTarget != null)
        {
            EmitDestructuringTarget(element.NestedTarget, access);
            return;
        }

        var name = element.Name!.Text;
        _state.Scope.AddIdentifier(name);
        _state.Prereq(new ConstVariable(name, null, access));
    }

    private void EmitObjectDestructuringField(ObjectDestructuringField field, LuauExpression access)
    {
        if (field.NestedTarget != null)
        {
            EmitDestructuringTarget(field.NestedTarget, access);
            return;
        }

        var name = field.BindingName.Text;
        _state.Scope.AddIdentifier(name);
        _state.Prereq(new ConstVariable(name, null, access));
    }

    /// <summary>
    ///     Tuple destructuring is kept out of the general array/object path above because it never needs a
    ///     table at all when the right-hand side already has its values in hand: a literal tuple binds each
    ///     name straight to its corresponding sub-expression, and a tuple-returning call already yields genuine
    ///     Luau multi-return values (whether that callee used the literal-return path or `table.unpack`
    ///     internally). Only a plain tuple-typed value (already living in a table) needs `table.unpack`.
    /// </summary>
    private LuauNode EmitTupleDestructuring(TupleDestructuringTarget target, Expression initializerExpression)
    {
        // A nested pattern is already rejected here by the parser (tuple destructuring does not support one),
        // so this only ever falls back to the placeholder for a program that already has that error.
        var names = target.Elements.ConvertAll(e => e.Name?.Text ?? "_");
        foreach (var name in names)
            _state.Scope.AddIdentifier(name);

        if (initializerExpression is TupleExpression tupleExpression)
        {
            for (var i = 0; i < names.Count; i++)
                _state.Prereq(new ConstVariable(names[i], null, Visit(tupleExpression.Expressions[i])));

            return new NoOpStatement();
        }

        var initializer = Visit(initializerExpression);
        if (initializerExpression is Invocation && _semanticModel.GetType(initializerExpression) is TupleType)
        {
            _state.Prereq(new MultiConstVariable(names, initializer));
            return new NoOpStatement();
        }

        var subject = _state.PushToVariable("_destructure", initializer);
        _state.Prereq(new MultiConstVariable(names, LuauFactory.TableCall("unpack", [subject])));
        return new NoOpStatement();
    }
}
