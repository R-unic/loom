# Contributing to Loom

First of all, thank you for considering contributing to Loom! However small the change, I appreciate it!

## Getting Started

Clone the repo, restore dependencies, and run the tests:

```bash
git clone https://github.com/rbx-loom/loom.git
cd loom
dotnet restore
dotnet build
dotnet test
```

If everything passes, you're good to go.

---

## Before You Write Code

Open an issue first. It doesn't have to be formal, just a quick description of what you want to do. This saves everyone time if the approach needs adjustment or
someone else is already working on it.

Check existing issues and PRs to make sure you're not duplicating work.

---

## Tests

The parser produces an AST, and the rest of the pipeline (resolver, type checker, Luau generator) walks that AST using the visitor pattern. Each stage depends
on the AST being structured correctly. If the parser produces a node incorrectly or errors are not reported via diagnostics, everything downstream breaks.

Changes to the parser often require updates to visitor implementations as well. Adding new syntax isn't solely parsing it and generating Luau, you also need to
handle it in the resolver and type checker.

Tests are required for all PRs.

### What to Test

Parser changes:

- Valid syntax parses correctly
- Invalid syntax produces errors
- The AST structure matches what you expect

Resolver changes:

- Nodes which declare symbols should test that the symbol is declared and properties match what is expected
- Scope rules are enforced
- What's expected to be allowed is allowed
- Code is semantically valid

Type checker changes:

- Type inference produces the right types
- Assignability passes or fails appropriately
- Type errors are reported clearly
- For new types:
    - Equals, IsAssignableTo, and ToString are tested

Code generation changes:

- The Luau AST represents the source correctly
- For new Luau nodes:
    - Rendering produces valid Luau
    - Edge cases are handled (escaping, formatting, empty collections, etc.)

---

## Code Style

I'm not strict about formatting, the linter handles that. A few things to keep in mind:

- Use PascalCase for classes, methods, and public properties
- Use camelCase for local variables and private fields
- Private fields should start with an underscore (except for private consts)
- Use meaningful names
- Avoid abbreviations in naming entirely
- Keep methods focused on their responsibility. If a method is doing too much, break it up.

---

## Pull Requests

Open your PR on the master branch. Include:

- A clear title and description
- A reference to the related issue
- Tests for your changes
- Updates to docs if needed

---

## Questions?

Open an issue with the question label or message me on Discord @_runic_. I'll get back to you.