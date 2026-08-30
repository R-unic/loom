using NuLua;
using NuLua.Luau;

namespace Loom.Testing.Runtime;

/// <summary>
///     <c>match</c> against an enum member by name (<c>Direction::North -> ...</c>) rather than its raw
///     underlying value. The emitted comparison is executed, not just read: what has to be right is that
///     each arm compares against the member's actual declared value, matching declaration order for a
///     plain enum and an explicit initializer for one that has it - neither is visible in the text of
///     the output alone if the wrong constant folded in by coincidence.
/// </summary>
[Collection("Assembly")]
public class EnumMatchPatternTest
{
    [Fact]
    public void MatchesEachMemberOfAPlainEnum_ByDeclarationOrder() =>
        Run(
            """
            enum Direction { North, South, East, West }

            fn opposite(d: Direction): Direction {
                return match d {
                    Direction::North -> Direction::South,
                    Direction::South -> Direction::North,
                    Direction::East -> Direction::West,
                    Direction::West -> Direction::East,
                };
            }

            let results = [
                opposite(Direction::North),
                opposite(Direction::South),
                opposite(Direction::East),
                opposite(Direction::West),
            ];
            """,
            """
            assert(results[1] == 1, "North's opposite should be South (1), got " .. results[1])
            assert(results[2] == 0, "South's opposite should be North (0), got " .. results[2])
            assert(results[3] == 3, "East's opposite should be West (3), got " .. results[3])
            assert(results[4] == 2, "West's opposite should be East (2), got " .. results[4])
            """
        );

    [Fact]
    public void MatchesAnExplicitlyValuedStringEnum() =>
        Run(
            """
            enum Status : string { Active = "active", Inactive = "inactive" }

            fn label(s: Status): string {
                return match s {
                    Status::Active -> "running",
                    Status::Inactive -> "stopped",
                };
            }

            let results = [label(Status::Active), label(Status::Inactive)];
            """,
            """
            assert(results[1] == "running", "Active should label as running, got " .. results[1])
            assert(results[2] == "stopped", "Inactive should label as stopped, got " .. results[2])
            """
        );

    /// <remarks>Falls through to the wildcard exactly when none of the named members match, same as a literal pattern would.</remarks>
    [Fact]
    public void FallsThroughToTheWildcard_WhenNoNamedMemberMatches() =>
        Run(
            """
            enum Direction { North, South, East, West }

            fn isNorthOrSouth(d: Direction): bool {
                return match d {
                    Direction::North -> true,
                    Direction::South -> true,
                    _ -> false,
                };
            }

            let results = [
                isNorthOrSouth(Direction::North),
                isNorthOrSouth(Direction::East),
                isNorthOrSouth(Direction::West),
            ];
            """,
            """
            assert(results[1] == true, "North should match, got " .. tostring(results[1]))
            assert(results[2] == false, "East should not match, got " .. tostring(results[2]))
            assert(results[3] == false, "West should not match, got " .. tostring(results[3]))
            """
        );

    private static void Run(string source, string assertions)
    {
        var emitted = Utility.GetLuauAST(source, true).Render();
        using var state = LuauState.Create();
        state.OpenLibraries();

        try
        {
            state.DoString($"{Strip(emitted)}\n{assertions}\n");
        }
        catch (Exception exception)
        {
            Assert.Fail($"the emitted match did not run: {exception.Message}\n\n{emitted}");
        }
    }

    /// <summary>
    ///     Drops what only the compiler's own output needs â€” the runtime require and the type aliases,
    ///     which name types the interpreter has no declarations for â€” and spells <c>const</c> the way
    ///     Luau does. Neither has any bearing on how the match runs.
    /// </summary>
    private static string Strip(string emitted) =>
        string.Join(
                '\n',
                emitted.Split('\n')
                    .Where(line => !line.Contains("require(", StringComparison.Ordinal))
                    .Where(line => !line.StartsWith("type ", StringComparison.Ordinal))
            )
            .Replace("const ", "local ");
}
