# AGENTS.md

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
    - `Modules/` — import/export graphing, resolution, and Luau require() path resolution
    - `Diagnostics/` — `DiagnosticBag`, severities, `InternalCodes.cs`. Errors flow through diagnostics, never exceptions (top-level `Compiler.Compile` catch =
      compiler bug path).
    - `Pipeline/` — `Compiler.cs` pipeline orchestration; `CompilationUnit.cs` multi-file compile driven by `LoomConfig` and two-phase parse-analyze step to
      support modules
- `Loom.Luau/` — Luau output AST + renderer (`LuauFactory`, `RenderState`, `AST/`)
- `Loom.Config/` — `loom-config.toml` reader (Tomlyn). `ProjectType` (default `game`), `Debug` (default `false`, for emitting debug diagnostics) `FilesConfig`:
  `SourceDirectory` (default `src`) → `OutputDirectory` (default `dist`)
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
    - `FilePaths.Same` — never compare paths with `==`. A client's `file:` URI round-trips a Windows drive letter in lower case while the compiler's
      path came from the project directory, so an ordinal comparison silently matches nothing
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
  default; only `Loom.CLI` opts in.
- The resolver keeps ambient names (intrinsics + `.d.loom` globals) in a scope below the file's own, so a module declaration shadows them instead of
  colliding. Scope depth is therefore not a test for "top level of a module" — use `AtModuleScope()`. Imports resolve ahead of the file's statements, so a name
  may be used above the import that brings it in.
- Output path derived by string-replacing source dir name with output dir name in the absolute path ([Compiler.cs:33](Loom.Core/Pipeline/Compiler.cs)) — fragile with
  nested same-named dirs.
- `Loom.TypeGenerator` generates intrinsic types from the Roblox API that the test suite relies on to pass. The intrinsics are stored in
  `Loom.Core/TypeChecking/Intrinsics`.
- PRs target `master`; open an issue before writing code (CONTRIBUTING.md).
