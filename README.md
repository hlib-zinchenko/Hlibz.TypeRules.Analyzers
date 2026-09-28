# Hlibz.TypeRules.Analyzers

Roslyn analyzers that enforce the **shape of a type based on what it inherits or implements**,
configured in `.editorconfig`:

> Every `IEndpoint` implementation must be `internal` and `sealed`.

Architecture-test libraries such as [ArchUnitNET](https://github.com/TNG/ArchUnitNET) and
[NetArchTest](https://github.com/BenMorris/NetArchTest) check rules like this when the tests run.
TypeRules checks them while you type: violations show up in the IDE, fail the build, and come with
a code fix.

```
error TR001: 'ListUsersEndpoint' is public, but rule set 'endpoints' allows at most internal
error TR002: 'ListUsersEndpoint' must be sealed, as required by rule set 'endpoints'
```

## Install

```bash
dotnet add package Hlibz.TypeRules.Analyzers
```

It's a development dependency: it runs at build time and adds nothing to your output. Requires the
.NET 8 SDK or later (Visual Studio 2022 17.8+, or a current Rider).

## Quick start

Add rule sets to your `.editorconfig`:

```ini
[*.cs]
# Endpoints: internal and sealed.
typerules.endpoints.match = T:MyApp.Api.IEndpoint
typerules.endpoints.max_accessibility = internal
typerules.endpoints.require_sealed = true

# Handlers: any construction of the generic interface, each with its own IFooHandler interface.
typerules.handlers.match = T:MyApp.Application.IRequestHandler`2
typerules.handlers.max_accessibility = internal
typerules.handlers.require_sealed = true
typerules.handlers.companion_interface = true
typerules.handlers.namespace_pattern = **.Handlers

# Entities: state changes only through their own methods.
typerules.entities.match = T:MyApp.Domain.Entity`1
typerules.entities.max_setter_accessibility = private
typerules.entities.readonly_collections = true
```

Nothing is checked until you configure a rule set, so installing the package on its own changes
nothing.

## Rules

| ID | Rule | Code fix |
|---|---|---|
| [TR000](docs/rules/TR000.md) | TypeRules configuration is invalid | — |
| [TR001](docs/rules/TR001.md) | Type is more accessible than its rule set allows | Change the access modifier |
| [TR002](docs/rules/TR002.md) | Type must be sealed | Add `sealed` |
| [TR003](docs/rules/TR003.md) | Type must implement its companion interface (`Foo` → `IFoo`) | Generate `IFoo`, or implement it |
| [TR004](docs/rules/TR004.md) | Setter is more accessible than its rule set allows | Restrict the accessor |
| [TR005](docs/rules/TR005.md) | Mutable collection (`List<T>`, arrays, ...) is exposed | Expose the read-only interface, when that still compiles |
| [TR006](docs/rules/TR006.md) | Type is declared in the wrong namespace | — (use the IDE's *Move to namespace*) |

All rules are warnings by default. With `TreatWarningsAsErrors`, they fail the build. Change a
rule's severity the usual way:

```ini
dotnet_diagnostic.TR002.severity = error
```

## Configuration

Each rule set is a group of keys named `typerules.<rule set>.<option>`. The rule-set name is yours
to choose: letters, digits, `_` and `-`.

| Option | Value | Meaning |
|---|---|---|
| `match` | One or more types, separated by `\|` or `,` | The rule set applies to every class, struct and record that inherits from or implements any of them, directly or indirectly |
| `max_accessibility` | `public`, `protected_internal`, `internal`, `protected`, `private_protected` or `private` | TR001: matched types can't be more accessible than this |
| `require_sealed` | `true` or `false` | TR002: matched non-abstract classes must be sealed |
| `companion_interface` | `true` or `false` | TR003: matched non-abstract types must implement an `I<TypeName>` interface, declared next to them, that extends a matched interface. Every `match` type must then be an interface |
| `max_setter_accessibility` | Same values as `max_accessibility` | TR004: `set` and `init` accessors of properties the matched types declare can't be more accessible than this |
| `allow_init` | `true` or `false` | TR004: exempt `init` accessors. Only valid with `max_setter_accessibility` |
| `readonly_collections` | `true` or `false` | TR005: properties and fields visible outside the matched types can't have a mutable collection type |
| `namespace_pattern` | Dot-separated names; `*` is one segment, `**` any number; alternatives separated by `\|` | TR006: matched types must be declared in a namespace matching one alternative, e.g. `**.Database.Configurations` |

A rule set needs `match` and at least one of the other options.

### Naming types in `match`

Use documentation-comment IDs, the same format CA rules use for their options:

| Type | `match` value |
|---|---|
| `MyApp.IEndpoint` | `T:MyApp.IEndpoint` (the `T:` prefix is optional) |
| `MyApp.Entity<TId>` | ``T:MyApp.Entity`1`` (backtick and number of type parameters) |
| `MyApp.IRequestHandler<TRequest, TResponse>` | ``T:MyApp.IRequestHandler`2`` |
| `Outer.Inner`, a nested type | `T:MyApp.Outer.Inner` |

A generic type matches every construction of it: ``T:MyApp.Entity`1`` matches `Entity<Guid>`
and `Entity<int>` alike.

### What gets checked

- **Classes, structs and records** that inherit from or implement a matched type. Interfaces that
  extend a matched interface are not checked, and neither is the matched type itself.
- **Abstract classes are checked by TR001** (an abstract endpoint base is still an endpoint) but
  not by TR002 or TR003: they can't be sealed, and nothing resolves them from DI.
- **Effective accessibility.** A `public` class nested inside an `internal` class is internal, so
  TR001 doesn't report it.
- **Partial types** are reported once. The TR001 fix rewrites every part that states an
  accessibility.
- **Members** (TR004, TR005) are checked on the type that declares them. Overrides and explicit
  interface implementations are skipped, since what they override or implement dictates their
  shape.
- **Generated code** (`// <auto-generated/>`, `*.g.cs`, source-generator output) is skipped,
  including the generated parts of partial types.
- When a type matches several rule sets, it has to satisfy all of them.

### Where configuration applies

Options are read per file, like any `.editorconfig` setting, so rule sets can differ between
folders. A `.globalconfig` works too, for rules that apply to a whole project.

One `.editorconfig` usually covers several projects, and not every project references every
matched type. So a `match` type the project can't see is skipped silently, unless the namespace
it's declared in *does* exist, in which case the name is most likely a typo and TR000 reports it.

### Invalid configuration

Anything TypeRules can't apply is reported as [TR000](docs/rules/TR000.md): unknown options, bad
values, types that don't resolve, types nothing can inherit from. A rule set with a problem is
ignored entirely rather than half-applied. TR000 has no source location, because it points at
configuration, not code, so it appears in build output rather than on a line in the editor.

## What TypeRules doesn't do

Some related checks already exist. Use these alongside TypeRules rather than expecting them here:

| Check | Use |
|---|---|
| Type name suffix by base type (every `IEndpoint` ends in `Endpoint`) | Built-in [CA1710](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1710) with `dotnet_code_quality.CA1710.additional_required_suffixes` and `api_surface = all` |
| Seal every internal type that has no subtypes | Built-in [CA1852](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1852). It's off in assemblies with `InternalsVisibleTo` unless you set `dotnet_code_quality.CA1852.ignore_internalsvisibleto = true` |
| Banned APIs (`DateTime.Now`, ...) | [RS0030](https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.BannedApiAnalyzers/BannedApiAnalyzers.Help.md) from Microsoft.CodeAnalysis.BannedApiAnalyzers |
| Layer and namespace dependencies | [NsDepCop](https://github.com/realvizu/NsDepCop), or ArchUnitNET/NetArchTest in tests |
| EF Core model conventions | [Hlibz.EntityFrameworkCore.ModelRules](https://github.com/hlib-zinchenko/Hlibz.EntityFrameworkCore.ModelRules) |

For example, the suffix half of the quick start above is:

```ini
dotnet_code_quality.CA1710.api_surface = all
dotnet_code_quality.CA1710.additional_required_suffixes = T:MyApp.Api.IEndpoint->Endpoint|T:MyApp.Application.IRequestHandler`2->Handler
```

## Roadmap

- **TR007** Matched types must not hold references to another rule set's types, e.g. an aggregate
  root holding another aggregate root instead of its id.

## Versioning

Rule IDs are permanent: never renumbered or reused. A rule may report more in a minor or patch
release when it was missing cases. New rules only report once you configure them, so upgrading
never adds diagnostics to a codebase that didn't opt in.

## License

[MIT](LICENSE)
