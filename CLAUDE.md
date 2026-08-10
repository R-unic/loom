# CLAUDE.md

Loom: domain-specific language for Roblox, transpiles to Luau. C# / .NET 10, xUnit tests. WIP — breaking changes allowed. Repo: https://github.com/rbx-loom/loom

## Commands

```bash
dotnet restore
dotnet build
dotnet test                     # full test suite (Loom.Testing, xUnit)
dotnet test --filter "FullyQualifiedName~ParserTest"   # single test class
dotnet run --project Loom.CLI -- <dir>                 # compile a Loom project (dir with loom-config.toml, default "."), TestProject exists for testing changes
dotnet run --project Loom.Tools -- ast <file.loom>     # dump AST for a file
dotnet run --project Loom.Tools -- generate-ast-snapshots  # regenerate AST snapshot files
```

CI (`.github/workflows/ci.yml`): `dotnet test -c Release` with coverage → Coveralls. Tests required for all PRs. Verify with `dotnet build` then `dotnet test`
before claiming done.

## Solution layout

- `Loom.Core/` — the compiler.
    - `Lexing/` — `Lexer`, rule-based (`LexerRules.cs`)
    - `Parsing/` — `Parser` (partial class split: `.Declarations`, `.Expressions`, `.Statements`, `.Types`), `AST/` one file per node type (~90 files)
    - `Resolving/` — `Resolver` (partial: `.ControlFlow`, `.Declarations`, `.Interfaces`, `.Modules`, `.Patterns`, `.SymbolTable`), `SemanticModel`, scopes +
      `Symbols/`
    - `FlowAnalysis/` — `FlowAnalyzer`, flow state for control-flow reasoning
    - `TypeChecking/` — `TypeChecker` (partial: `.Enums`, `.Generics`, `.Interfaces`, `.ControlFlow`, `.Declarations`, `.Invocations`, `.Match`, `.MemberAcess`,
      `.Operators`, `.TypeNodes`, , `.Check` (bidirectional/contextual typing)), plus `TypeInferrer`, `TypeNarrower`, `TypeSimplifier`, `TypeSolver`; Intrinsics
      for injecting .loom files into all Loom programs
      `Types/` one file per type kind (union, intersection, literal, generic, etc.); `Intrinsic/` + operator binders/rules
    - `Generation/` — `LuauGenerator` (partial: `.Declarations`, `.Events`, `.Expressions`, `.Interfaces`, `.Match`, `.Statements`, `.Types`),
      `LuauOperatorMap.cs`, `Macros/` with `IMacroProvider` implementations under `Macros/Providers/` (Array, Range, Number, Result, Instance, global
      invocations)
    - `Modules/` — import/export graphing, resolution, and Luau require() path resolution. A relative specifier
      resolves inside the importing file's own root; a bare one (`math`, `scope/math`, `math/vector`) names the root
      publishing that package — its `init.loom` when no subpath follows — and only a package the importing root
      declares in `[dependencies]` is importable
    - `Diagnostics/` — `DiagnosticBag`, severities, `InternalCodes.cs`. Errors flow through diagnostics, never exceptions (top-level `Compiler.Compile` catch =
      compiler bug path).
    - `Pipeline/` — `Compiler.cs` pipeline orchestration; `CompilationUnit.cs` multi-file compile with a two-phase parse-analyze step to support modules.
      A unit spans a `SourceRootSet`: one `SourceRoot` (a `LoomConfig` plus the files under its source directory) per project it compiles — the entry
      project, plus one per source-distributed dependency. The boundary a relative import may not cross comes from `Roots.Of(file)`, never from the unit's
      own `Config`; where a file's Luau goes is `Roots.OutputPathOf(file)`'s single answer, used by both the writer and the require-path resolver.
      **Install-location contract:** a dependency's output is written into the *entry* project's output directory, under
      `<output>/packages/<scope>/<name>` — compiled output is consumer-specific (it names the entry project's runtime and is checked against its project
      type's intrinsics), so it cannot live beside sources a package manager may share. One `$path` covering the project's output therefore covers every
      package, whatever the PM did with the sources. `no_emit` is read off the entry project alone for the same reason
- `Loom.Luau/` — Luau output AST + renderer (`LuauFactory`, `RenderState`, `AST/`)
- `Loom.Config/` — `loom-config.toml` reader (Tomlyn). `ProjectType` (default `game`), `Debug` (default `false`, for emitting debug diagnostics) `FilesConfig`:
  `SourceDirectory` (default `src`) → `OutputDirectory` (default `dist`). Package identity lives here too: `[package]` (`PackageConfig`, with `PackageName` and
  semver `Version` value types, plus `Realm`), `[dependencies]` (`Dependency`), `[registry]` (`RegistryConfig`). `ConfigReader` never throws on a manifest
  problem — malformed manifests come back as `null` plus `ConfigDiagnostic`s out of `LocateFromDirectory`. `[files]` directories are validated there too
  (non-empty, relative, path-legal), since nothing downstream can report one that isn't: they are resolved as real paths and a stage throwing is the
  compiler-bug path
- `Loom.CLI/` — entry point; locates config, compiles unit, prints debug info. `Include/loom_runtime.luau` = runtime support emitted alongside output
- `Loom.LanguageServer/` — LSP server (OmniSharp). One handler per request, all registered in `Program.cs`, all answering off the `DocumentStore`:
  it keeps one `CompilationUnit` per project root and recompiles the open file on every change. The pieces the handlers share:
    - `CompletionSnapshot` — rebuilt from each compile; answers "what may be written at this offset" (member scope, import list, attribute list,
      module specifier, type vs value position — plus keywords and built-in type names, which are never symbols, and `Importable`, the names other
      modules export that this file has not imported). A completion item's detail and documentation are `Func<>`s resolved on the client's resolve
      request, never eagerly: a project has thousands of names in scope on every keystroke and the client reads at most one
    - `DeclarationDisplay` renders a symbol the way its declaration reads; `SymbolMarkdown` composes the hover body; `DocumentationBlock` parses
      `@param`/`@returns` out of a doc comment; `CallSiteFinder` locates the call the cursor is inside and who it calls
    - `SymbolReferences` walks every analyzed tree to invert the resolver — what refers to *this* symbol — behind references, rename, prepare-rename
      and document highlight. An import binds the exporting module's own `Symbol` instance, so one identity spans every file; a reference whose token
      text differs from the symbol's name is an alias and is left alone by rename
    - `ImportCatalog`/`ImportEdits` decide what a file could import and build the edit that imports it, shared by auto-import completion and the
      "Import 'X'" quick fix
    - `DocumentStore` splits recording an edit from compiling it: `Change` only records, `Compile`/`TryGetState` bring the document up to date on
      demand, and only `didChange`'s diagnostic publish is deferred (via `Debouncer`) because it is the one thing nobody is blocked on. Every dirty
      buffer of a unit goes into one `Recompile` together, and `Close` reverts the file to its saved text — the unit keeps whatever text it was last
      handed, and an editor discards unsaved edits when a document closes
    - `DiagnosticPublisher` reports every file the compile found something in, not just the edited one, and remembers what it said so a file whose
      errors are gone gets an empty set. It only ever clears files the compile covered: a workspace may hold more than one project
    - `Conversion` owns `DiagnosticsFor`/`DiagnosticsByFile`: a null result means the file was not analyzed, and an empty set is how a client is told
      to drop what it is still showing
    - `WatchedFilesHandler` takes the client's file-watcher notifications (the client watches, not the server — it already knows the user's exclusions
      and would not hear its own writes back) and feeds them to `DocumentStore.ReloadFromDisk`, debounced so a branch switch compiles once rather than
      once per file. A file open in the editor is skipped: its buffer is what every request is answered against
    - `FilePaths.Same`, which is `Loom.Core.Text.PathComparison` — never compare paths with `==`. A client's `file:` URI round-trips a Windows drive
      letter in lower case while the compiler's path came from the project directory, so an ordinal comparison silently matches nothing. *Module
      specifiers* are deliberately case-sensitive, though (Roblox requires are), so `SourceRootSet.CanonicalPath` is what keeps a path entering the
      unit from a new source spelled the way the roots already spell it
- `Loom.TypeGenerator/` — Loom code generator to define types for the Roblox API; tests depend on these types to be generated to pass
- `Loom.Tools/` — dev tooling (AST dump, snapshot generation)
- `Loom.Testing/` — all tests, one test class per compiler stage/component
- `TestProject/` — sample Loom project (src/dist + loom-config.toml) for end-to-end runs

## Pipeline

`Lexer → Parser → Resolver → FlowAnalyzer → TypeChecker → LuauGenerator → LuauTree.Render()` (see [Compiler.cs](Loom.Core/Pipeline/Compiler.cs)). Every stage returns a
result carrying a `DiagnosticBag`; stages after the parser walk the AST via the visitor pattern. New syntax means touching parser AND resolver AND type checker
AND generator — not just parse + emit (see CONTRIBUTING.md).

## Tests

- Framework: xUnit + coverlet. Shared helpers in [Utility.cs](Loom.Testing/Utility.cs).
- Snapshot tests: `Loom.Testing/Snapshots/AST/*.loom` + `.ast` pairs (parser), `Snapshots/Luau/*.loom` + `.luau` pairs (full-pipeline codegen). Adding a
  language feature usually adds a snapshot pair. Regenerate AST snapshots with Loom.Tools.
- Per-stage expectations (from CONTRIBUTING.md): parser — valid parses/invalid errors/AST shape; resolver — symbols declared, scope rules; type checker —
  inference, assignability, and for new types test `Equals`, `IsAssignableTo`, `ToString`; codegen — Luau AST correct, rendering valid, edge cases (escaping,
  empty collections).
- Round-trip tests: `Loom.Testing/Runtime/*.luau` assertion bodies pair by name with a `Snapshots/Luau` case and actually *execute* the emitted serializers
  on an embedded Luau ([SerializationRuntimeTest.cs](Loom.Testing/SerializationRuntimeTest.cs)). Snapshots only prove the output did not change; these prove it
  works, and caught several bugs snapshots could not (wrong value shape, a shadowed local, an undersized buffer). The interpreter comes from the `NuLua.Luau`
  package, so they need nothing installed and run under a plain `dotnet test`.

## Conventions

- PascalCase classes/methods/public properties; camelCase locals; private fields `_underscore` prefixed (except private consts); no abbreviations in names.
- Nullable + ImplicitUsings enabled everywhere; primary constructors used (e.g. `Compiler(CompilationUnit unit, SourceFile file)`).
- Big classes split as partial files by concern (`Parser.Expressions.cs`, `TypeChecker.Generics.cs`) — follow that pattern when a stage grows.
- One AST node / one type kind per file.
- Commit style: conventional-commit prefixes `feat:`/`fix:`/`test:`/`docs:`/`ref:` (see git log).
- Source files: Loom source uses `.loom` extension; output `.luau`. Indices are 1-based (Luau semantics). Immutability by default (`let` → `const`/local, `mut`
  for mutable).
- Loom comments: `##` line, `#: … :#` block, `###` doc. A run of `###` lines documents the declaration below it and is the only comment form anything
  reads — the lexer pairs each run with the token it precedes in `SourceFile.Documentation`, and `Node.Documentation` looks it up. `@param name text`
  and `@returns text` inside one are pulled out for signature help.
- ReSharper/Rider settings in `Loom.sln.DotSettings`; formatting handled by linter, don't hand-fight it.

## Gotchas

- Testing imports both plus `Type = Loom.TypeChecking.Types.Type` alias to dodge `System.Type` clash.
- `DiagnosticOptions.FailFast` (per `CompilationUnit`, threaded into every stage's `DiagnosticBag`) prints the first error and exits the process. Off by
  default; only `Loom.CLI` opts in. Options are handed out per file by `CompilationUnit.DiagnosticOptionsFor` — a dependency's files never fail fast, so the
  error the build stops on is the one naming the package.
- A dependency's diagnostics are not the consumer's to fix: `Compiler` runs every dependency file's bag through `DiagnosticBag.AttributedTo`, which drops
  warnings and info and collapses errors into one `PackageFailedToCompile` per file carrying the first underlying error. `DiagnosticOptions
  .ReportDependencyDiagnostics` (CLI: `--dependency-diagnostics`) turns that off for debugging a package from a project consuming it. Opening a package's own
  files in the LSP needs no flag — the package is the entry root of its own unit there.
- The resolver keeps ambient names (intrinsics + `.d.loom` globals) in a scope below the file's own, so a module declaration shadows them instead of
  colliding. Scope depth is therefore not a test for "top level of a module" — use `AtModuleScope()`. Imports resolve ahead of the file's statements, so a name
  may be used above the import that brings it in.
- `.d.loom` globals are scoped to the root that declared them (`GlobalSymbols`, keyed by name *and* namespace): a package cannot put ambient names in a
  consumer's scope — its public surface is its exports — and one name declared by two of a root's declaration files is an error. Intrinsics are not
  root-scoped; they reach every file of every root.
- Output path derived via `Path.GetRelativePath` from the source directory, then re-rooted under the output directory
  ([FileManager.cs:19](Loom.Core/Pipeline/FileManager.cs)).
- `Loom.TypeGenerator` generates intrinsic types from the Roblox API that the test suite relies on to pass. The intrinsics are stored in
  `Loom.Core/TypeChecking/Intrinsics`.
- A Tomlyn `TomlConverter` may only read a *scalar* value. One that consumes a table (inline or not) desynchronizes the reader and silently swallows the
  table that follows it — which is why `[dependencies]` binds as `Dictionary<string, object>` and is read in `ConfigReader` instead of by a converter.
- PRs target `master`; open an issue before writing code (CONTRIBUTING.md).
