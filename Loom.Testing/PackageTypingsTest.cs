namespace Loom.Testing;

/// <summary>
///     <c>Snapshots/Packages/jecs.d.loom</c> - a hand-written declaration file for a real, popular Luau
///     package (jecs, an Entity Component System) - against a program that exercises essentially all of
///     it: every overload arity of <c>entity</c>/<c>get</c>/<c>has</c>/<c>query</c>, every query type
///     (0-4 components) and its <c>with</c>/<c>without</c>/<c>cached</c>/<c>archetypes</c>, relationship
///     pairs, the <c>added</c>/<c>changed</c>/<c>removed</c> hooks, and a <c>for</c> loop over the
///     functional iterator <c>query.iter()</c> returns. A single typings file this size stresses the
///     type checker (deep generics, overload sets, unions as component ids, functional iterators) far
///     more than any one hand-written test does, which is what caught the three bugs the fixes alongside
///     this file exist for - a cross-file ambient global's type, an overloaded generic candidate, and a
///     shared inference cycle guard. Kept as a project file rather than an inline string so it stays the
///     one thing a consumer would actually copy into their own project.
/// </summary>
[Collection("Assembly")]
public class PackageTypingsTest
{
    private static readonly string JecsDeclaration = File.ReadAllText(Path.Combine(AssemblyFixture.Snapshots, "Packages", "jecs.d.loom"));

    private const string Usage = """
        let world = jecs.world(true);
        world.range(256, 1000);

        let Position = world.component::<Vector3>();
        let Velocity = world.component::<Vector3>();
        let Health = world.component::<number>();
        let Name = world.component::<string>();
        let Player = jecs.tag();

        let e = world.entity();
        let reentered = world.entity(e);
        let fromRaw: Entity = world.entity(42);

        world.add(e, Player);
        world.set(e, Position, Vector3.create(0, 0, 0));
        world.set(e, Velocity, Vector3.create(1, 0, 0));
        world.set(e, Health, 100);
        world.set(e, Name, "hero");

        let parentTag = jecs.pair(jecs.child_of, Player);
        world.add(e, parentTag);
        let isRelationship: bool = jecs.is_pair(parentTag);
        let predicate = jecs.pair_first(world, parentTag);
        let target = jecs.pair_second(world, parentTag);

        let pos: Vector3? = world.get(e, Position);
        let (pos2, vel2): (Vector3?, Vector3?) = world.get(e, Position, Velocity);
        let (pos3, vel3, hp3): (Vector3?, Vector3?, number?) = world.get(e, Position, Velocity, Health);
        let (pos4, vel4, hp4, name4): (Vector3?, Vector3?, number?, string?) = world.get(e, Position, Velocity, Health, Name);

        let has1: bool = world.has(e, Position);
        let has2: bool = world.has(e, Position, Velocity);
        let has3: bool = world.has(e, Position, Velocity, Health);
        let has4: bool = world.has(e, Position, Velocity, Health, Name);

        let parent: Entity? = world.parent(e);
        let targetOf: Entity? = world.target(e, jecs.child_of);
        let targetIndexed: Entity? = world.target(e, jecs.child_of, 0);

        let doesContain: bool = world.contains(e);
        let doesExist: bool = world.exists(e);

        for kid: world.targets(e, jecs.child_of) {
            print(kid);
        }

        for child: world.children(Player) {
            print(child);
        }

        for tagged: world.each(Player) {
            print(tagged);
        }

        let all = world.query();
        for onlyId: all.iter() {
            print(onlyId);
        }

        let q1 = world.query(Position);
        for id1, position1: q1.iter() {
            print(id1, position1);
        }

        let q2 = world.query(Position, Velocity)
            .with(Health)
            .without(Name);

        for id2, position2, velocity2: q2.iter() {
            world.set(id2, Position, position2 + velocity2);
        }

        let q3 = world.query(Position, Velocity, Health);
        for id3, position3, velocity3, health3: q3.iter() {
            print(id3, position3, velocity3, health3);
        }

        let q4 = world.query(Position, Velocity, Health, Name);
        for id4, position4, velocity4, health4, name4b: q4.iter() {
            print(id4, position4, velocity4, health4, name4b);
        }

        let cached = q2.cached();
        for cachedId, cachedPosition, cachedVelocity: cached.iter() {
            print(cachedId, cachedPosition, cachedVelocity);
        }

        let archetypes = q2.archetypes();
        for archetype: archetypes {
            print(archetype.id, archetype.type_key, archetype.entities);
        }

        let cachedArchetypes = cached.archetypes(true);
        print(cachedArchetypes);
        cached.fini();

        let wasSeen: bool = q2.has(e);

        let stopAdded = world.added(Position, fn(entity, id, value) {
            print(entity, id, value);
        });
        let stopChanged = world.changed(Position, fn(entity, id, value) {
            print(entity, id, value);
        });
        let stopRemoved = world.removed(Position, fn(entity, id, deleted) {
            print(entity, id, deleted);
        });
        stopAdded();
        stopChanged();
        stopRemoved();

        world.remove(e, Velocity);
        world.clear(reentered);
        world.cleanup();
        world.delete(e);
        world.delete(fromRaw);
        """;

    [Fact]
    public void CompilesCleanly_AgainstAComprehensiveUsageProgram() =>
        Utility.WithTempProject(
            [("jecs.d.loom", JecsDeclaration), ("usage.loom", Usage)],
            (_, result) => Utility.AssertNoErrors(result)
        );
}
