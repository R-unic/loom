using NuLua;
using NuLua.Luau;

namespace Loom.Testing;

/// <summary>
///     Runs the emitted futures instead of only reading them. A snapshot can say that a call became
///     <c>Loom.future</c>; it cannot say that two of them are actually in flight together, that waiters
///     are released in the right order, or that awaiting an already-settled future does not park the
///     caller forever - which is all the behaviour there is.
/// </summary>
/// <remarks>
///     <para>
///         Unlike <see cref="SerializationRuntimeTest" />, whose prelude reimplements the runtime helpers
///         it needs, this evaluates the real <c>loom_runtime.luau</c>: the scheduling is the thing under
///         test, so a second copy of it would only ever test itself. The file is Luau apart from
///         <c>const</c>, which Luau has no keyword for, and its <c>require</c> of the event module, which
///         nothing here reaches.
///     </para>
///     <para>
///         Roblox's <c>task</c> is stubbed rather than modelled: <c>Loom.future</c> uses only
///         <c>task.spawn</c>, whose whole contract is "resume this now", so resuming it now is the stub.
///     </para>
/// </remarks>
[Collection("Assembly")]
public class FutureRuntimeTest
{
    private static readonly string _runtimeDirectory = $"..{Path.DirectorySeparatorChar}..{Path.DirectorySeparatorChar}..{Path.DirectorySeparatorChar}Runtime";
    private static readonly string _includeDirectory = Path.Combine("..", "..", "..", "..", "Loom.Core", "Pipeline", "Include");

    public static IEnumerable<TheoryDataRow<string>> Cases =>
        Directory.EnumerateFiles(_runtimeDirectory, "future_*.luau")
            .Select(path => new TheoryDataRow<string>(Path.GetFileNameWithoutExtension(path)));

    [Theory]
    [MemberData(nameof(Cases))]
    public void Runs(string caseName)
    {
        var emitted = File.ReadAllText(Path.Combine(AssemblyFixture.Snapshots, "Luau", $"{caseName}.luau"));
        var assertions = File.ReadAllText(Path.Combine(_runtimeDirectory, $"{caseName}.luau"));

        using var state = LuauState.Create();
        state.OpenLibraries();

        try
        {
            state.DoString(Assemble(emitted, assertions));
        }
        catch (Exception exception)
        {
            Assert.Fail($"{caseName} did not run: {exception.Message}");
        }
    }

    /// <summary>
    ///     Splices the real runtime, the emitted code, and the assertions into one chunk.
    /// </summary>
    /// <remarks>
    ///     The assertions run inside a coroutine rather than at the top level, because that is where they
    ///     run in Roblox too: <c>Loom.await</c> parks its caller with <c>coroutine.yield</c>, which the main
    ///     thread cannot do. A script body in Roblox is already a thread that may yield, so awaiting from
    ///     one is exactly what the emitted code expects.
    /// </remarks>
    private static string Assemble(string emitted, string assertions)
    {
        // the runtime is a module, so it ends by returning its table - and Luau requires 'return' to be the
        // last statement of a block, so the emitted code could not follow it. Dropping that one line leaves
        // 'local Loom' in scope, which is what the emitted code names anyway.
        var runtime = string.Join(
            '\n',
            File.ReadAllText(Path.Combine(_includeDirectory, "loom_runtime.luau"))
                .Split('\n')
                .Where(line => !line.Contains("require(", StringComparison.Ordinal))
                .Where(line => line.Trim() != "return Loom")
        );

        var body = string.Join(
            '\n',
            emitted.Split('\n').Where(line => !line.Contains("require(", StringComparison.Ordinal))
        );

        return $"""
                {TaskStub}
                -- runtime
                {runtime}
                -- emitted
                {body}
                -- assertions
                local function assertions()
                {assertions}
                end

                local failure
                task.spawn(function()
                	local ok, err = pcall(assertions)
                	if not ok then
                		failure = err
                	end
                end)

                if failure ~= nil then
                	error(failure, 0)
                end
                """.Replace("const ", "local ");
    }

    private const string TaskStub =
        """
        task = {}
        function task.spawn(target, ...)
        	local thread = if type(target) == "thread" then target else coroutine.create(target)
        	local ok, err = coroutine.resume(thread, ...)
        	if not ok then
        		error(err, 0)
        	end

        	return thread
        end
        """;
}
