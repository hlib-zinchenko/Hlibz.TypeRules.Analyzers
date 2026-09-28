# CLAUDE.md

Guidance for Claude Code when working in this repository.

## What this is

A NuGet package of Roslyn analyzers that enforce the shape of a type (accessibility, sealing, ...)
based on what it inherits or implements. Rule sets are configured in `.editorconfig` as
`typerules.<rule set>.<option>` keys. See README for the user-facing contract.

## Requires

* .NET 10 SDK. The analyzers target netstandard2.0 and the tests net10.0.

## Commands

```bash
# Build everything
dotnet build

# Run the tests
dotnet test --project tests/Hlibz.TypeRules.Analyzers.Tests

# End-to-end: the sample must build clean, and must fail with TR001/TR002 with its violations
dotnet build samples/Hlibz.TypeRules.Analyzers.Sample
dotnet build samples/Hlibz.TypeRules.Analyzers.Sample -p:IncludeViolations=true

# Pack locally
dotnet pack src/Hlibz.TypeRules.Analyzers.Package -c Release -o ./nupkg
```

Test projects use Microsoft Testing Platform (`test.runner` in `global.json`), so `dotnet test`
needs `--project <path>` rather than a bare directory argument.

## Architecture

- **Three projects in `src/`**, following the Roslyn analyzer template:
  - `Hlibz.TypeRules.Analyzers`: the analyzers. Must not reference Microsoft.CodeAnalysis.Workspaces
    (RS1038), because the compiler loads it without Workspaces. Its `PackageId` is
    `Hlibz.TypeRules.Analyzers.Internal` only so NuGet restore doesn't confuse it with the package
    project's `PackageId`; it's never packed itself.
  - `Hlibz.TypeRules.Analyzers.CodeFixes`: code fixes, which need Workspaces.
  - `Hlibz.TypeRules.Analyzers.Package`: builds nothing, packs both DLLs into
    `analyzers/dotnet/cs` under the `Hlibz.TypeRules.Analyzers` package ID.
- **Roslyn version is the support floor.** The analyzers compile against Microsoft.CodeAnalysis
  4.8 (.NET 8 SDK, VS 17.8), pinned in `Directory.Packages.props`. Don't raise it for convenience;
  every API used must exist in 4.8. The tests run on the same version.
- **netstandard2.0 constraints** in the analyzer and code-fix projects: no ranges/indices
  (`s[..n]`), no `init`/records (no `IsExternalInit`), no `string.Contains(char)`, no
  `[NotNullWhen]`. Use `Substring`, sealed classes with constructors, `IndexOf`.
- **`TypeRulesAnalyzer`** is one analyzer for every rule, because they share configuration
  parsing. Per compilation, `CompilationAnalyzer` caches one parsed `TypeRulesConfiguration` per
  `AnalyzerConfigOptions` instance (options are per file) and reports each TR000 message once.
  A type's options come from the file of its first declaration.
- **`Configuration/ConfigurationParser`** reads `typerules.*` keys (arriving lowercased; values
  keep their case), groups them by rule set and resolves `match` entries with
  `DocumentationCommentId`. A rule set with any error is dropped entirely. An unresolved type is an
  error only if its containing namespace or type exists (probably a typo); otherwise it's silently
  skipped, because one `.editorconfig` spans projects that can't all see every type.
- **`AccessScope`** models accessibility as flags for where a type can be seen from, because C#
  accessibility is only partially ordered (`protected` vs `internal`). "Exceeds the maximum" is
  `IsWithin` (a subset check), effective accessibility is the intersection along the containing
  types, and several rule sets' maximums combine by intersection too.
- **Code fixes only fix when it's safe.** The TR001 fix picks the most permissive level within the
  maximum that's legal where the type is declared (no protected levels inside structs, static or
  sealed classes); the TR002 fix isn't offered when anything derives from the type or it declares
  new virtual/protected members; the TR003 fix only acts when the companion is missing or
  exists and is valid; the TR004 fix skips required, interface-implementing, virtual and
  getter-less properties; the TR005 fix only registers when the change still compiles. Tests cover each "no fix offered" case.
- **`CompanionInterfaces`** (TR003) holds the companion lookup (`I` + name, same arity, same
  namespace or containing type) shared by the analyzer and `CompanionInterfaceCodeFixProvider`,
  and passes the fix its state (`Missing`/`NotImplemented`; absent = no safe fix) and the matched
  interfaces' documentation IDs as diagnostic properties. The fix generates `IFoo.cs` through
  `ImportAdder`/`Simplifier`/`Formatter`, then normalizes to the source file's line endings,
  because those APIs use the workspace default. It has its own `FixAllProvider`: the batch fixer
  drops added documents, so it re-resolves each type by documentation ID and applies the fixes
  one after another. Roslyn 4.8 has `ImportAdder.AddImportsAsync` with
  `Simplifier.AddImportsAnnotation`, not the newer `AddImportsFromSymbolAnnotationAsync`.
- **`MemberRules`** (TR004, TR005) checks the properties and fields a matched type declares,
  skipping overrides and explicit interface implementations. Generated partial parts need no
  check of their own: with `GeneratedCodeAnalysisFlags.None`, the analyzer driver drops
  diagnostics located in generated code (a test pins this). `MutableCollections` is TR005's
  explicit list of mutable types (not "implements ICollection<T>": immutable collections do too)
  and their read-only equivalents, shared with the fix.
- **The TR005 fix compiles speculatively.** Changing a member's type can break callers, so
  `ExposeReadOnlyCollectionCodeFixProvider` applies the change and compares error counts in every
  document referencing the member (`SymbolFinder.FindReferencesAsync`) before registering.
- Code-fix helpers shared across fixes: `Accessibilities` (where protected levels are legal) and
  `DocumentCleanup` (imports, simplification, formatting, line-ending normalization).
- **`Configuration/NamespacePattern`** (TR006) matches namespaces segment by segment: `*` is one
  segment, `**` any number including none (so `**` alone matches the global namespace). TR006
  reports once per violated rule set, since namespace requirements can't be combined the way
  accessibility maximums are, and has no code fix: moving a type means moving callers and the
  file, which the IDE's own refactoring does.
- **Rule IDs `TR000`–`TR006` are public contract.** Never renumber or reuse one. Descriptors live
  in `Descriptors.cs`, with help links to `docs/rules/<ID>.md`.

## Tests

`tests/Hlibz.TypeRules.Analyzers.Tests` uses Microsoft.CodeAnalysis.Testing via `Verifier`, which
takes configuration as `.editorconfig` lines and adds `Verifier.Contracts` (the `App.*` base types
the tests match on) to every compilation. Diagnostics are marked `{|#0:Name|}` and expected with
`.WithLocation(0)`; TR000 has no location. To assert that no fix is offered, pass the same sources
as the fixed sources; files a fix adds go in `addedFiles` by file name. Test names follow `Subject_WithCondition_ExpectedOutcome`, e.g.
`Fix_WithDerivedClass_OffersNoFix`.

`samples/Hlibz.TypeRules.Analyzers.Sample` references the analyzer project as an analyzer, is
configured by its own (non-root) `.editorconfig`, and covers what unit tests can't: real
`.editorconfig` loading and build integration. CI builds it clean, then with
`-p:IncludeViolations=true` and checks TR001 and TR002 are reported.

## Adding a rule

1. Take the next free ID and add a descriptor to `Descriptors.cs` and `SupportedDiagnostics`.
2. Add the option(s) to `ConfigurationParser` (`KnownOptions`, parsing, the "sets no constraint"
   check) and to `TypeRuleSet`.
3. Add the check to `TypeRulesAnalyzer`, and a code fix in the CodeFixes project if one is safe.
4. Add the ID to `AnalyzerReleases.Unshipped.md` (RS2008 fails the build otherwise).
5. Tests, `docs/rules/<ID>.md`, README's rules and configuration tables, and a case in the
   sample's `Violations/`.

## Releasing

`.github/workflows/release.yml` runs on a pushed `vX.Y.Z` tag (or `vX.Y.Z-preview.N`). It builds,
tests, packs `src/Hlibz.TypeRules.Analyzers.Package` with `-p:Version` from the tag, and publishes
through NuGet Trusted Publishing (OIDC, no API key). It uses the `release` GitHub Environment,
which must match the Environment on the nuget.org trusted-publishing policy, and a
`${{ secrets.NUGET_USER }}` repo secret with the nuget.org profile username.

Before tagging, move the release's rules from `AnalyzerReleases.Unshipped.md` into
`AnalyzerReleases.Shipped.md` under `## Release X.Y.Z`. Before the first release, also remove the "Not on NuGet
yet" note from README's Install section: the README is packed into the package. Don't bump the csproj version; the tag
drives it. Then `git tag vX.Y.Z && git push origin vX.Y.Z`.

## Conventions

- File-scoped namespaces, explicit types (no `var`), 4-space indent, LF, UTF-8 (no BOM), 100-char
  lines. See `.editorconfig`.
- `Directory.Packages.props` manages package versions centrally.
- No StyleCop/SonarAnalyzer, same as ModelRules: `TreatWarningsAsErrors` plus the RS analyzer
  rules from Microsoft.CodeAnalysis.Analyzers.
