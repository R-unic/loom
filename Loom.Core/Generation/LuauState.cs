using Loom.Luau.AST;

namespace Loom.Core.Generation;

internal sealed class LuauState
{
    public LuauScope Scope = new();

    public Identifier PushToVariable(string name, LuauExpression expression, LuauType? type = null, bool isConst = true)
    {
        if (expression is Identifier identifier)
            return identifier;

        var id = Scope.AddIdentifier(name);
        Prereq(isConst ? new ConstVariable(id, type, expression) : new LocalVariable(id, type, expression));
        return new Identifier(id);
    }

    public (T Node, LuauScope Scope) Capture<T>(Func<T> callback)
    {
        T value = default!;
        var scope = CaptureScope(() => value = callback(), isolated: false);
        return (value, scope);
    }

    /// <summary>
    /// Used specifically where a new Luau function body begins, so its locals don't compare
    /// against (and don't get seen by) anything outside that function.
    /// </summary>
    public T CaptureIsolated<T>(Func<T> callback) => CaptureIsolatedScope(callback).Node;

    public (T Node, LuauScope Scope) CaptureIsolatedScope<T>(Func<T> callback)
    {
        T value = default!;
        var scope = CaptureScope(() => value = callback(), isolated: true);
        return (value, scope);
    }

    private LuauScope CaptureScope(Action callback, bool isolated)
    {
        var captured = new LuauScope(Scope, isolated);
        Scope = captured;
        try
        {
            callback();
        }
        finally
        {
            Scope = captured.Parent!;
        }

        return captured;
    }

    public void Prereq(params LuauStatement[] statements) => Scope.PrereqStatements.AddRange(statements);
    public void Postreq(params LuauStatement[] statements) => Scope.PostreqStatements.AddRange(statements);
}