using Loom.Luau.AST;

namespace Loom.Core.Generation;

internal sealed class LuauScope(LuauScope? parent = null, bool isolated = false)
{
    // Non-isolated scopes share their parent's name table, so sibling statements within the same
    // real Luau block (e.g. two prerequisite-generating statements in one function body) still see
    // each other's synthesized names. Isolated scopes get a fresh table instead - used specifically
    // where a genuinely new Luau function scope begins, so unrelated function bodies (which don't
    // share a real Luau scope) don't evade collisions against each other for no reason.
    private readonly Dictionary<string, int> _nameCounts = isolated || parent == null ? [] : parent._nameCounts;

    public LuauScope? Parent { get; } = parent;
    public List<LuauStatement> PrereqStatements { get; } = [];
    public List<LuauStatement> PostreqStatements { get; } = [];

    public string AddIdentifier(string name)
    {
        if (TryGetId(name, out var temporaryId))
            return name + "_" + (_nameCounts[name] = temporaryId + 1);

        _nameCounts[name] = 0;
        return name;
    }

    public void Reserve(string name) => _nameCounts.TryAdd(name, 0);

    private bool TryGetId(string name, out int temporaryId) => _nameCounts.TryGetValue(name, out temporaryId);
}