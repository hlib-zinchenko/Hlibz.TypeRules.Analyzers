# Hlibz.TypeRules.Analyzers

[![CI](https://github.com/hlib-zinchenko/Hlibz.TypeRules.Analyzers/actions/workflows/ci.yml/badge.svg)](https://github.com/hlib-zinchenko/Hlibz.TypeRules.Analyzers/actions/workflows/ci.yml)

Roslyn analyzers that enforce the **shape of a type based on what it inherits or implements**,
configured in `.editorconfig`:

- every `IEndpoint` implementation is `internal`, `sealed` and lives in an `Endpoints` namespace
- every request handler has its own `IFooHandler` interface, for dependency injection
- entities don't expose public setters or mutable collections, and refer to each other by id
- value objects are immutable, compare by value, and are created through factory methods

Architecture-test libraries such as [ArchUnitNET](https://github.com/TNG/ArchUnitNET) and
[NetArchTest](https://github.com/BenMorris/NetArchTest) check rules like these when the tests run.
TypeRules checks them while you type: violations show up in the editor and fail the build, and
most come with a code fix.

```
error TR101: 'ListUsersEndpoint' is public, but rule set 'endpoints' allows at most internal
error TR104: 'ListOrdersHandler' must implement a companion interface 'IListOrdersHandler' that extends 'IRequestHandler<TRequest, TResponse>', as required by rule set 'handlers'
error TR201: Constructor 'Money(decimal, string)' is public, but rule set 'value-objects' allows at most private
error TR202: 'Money' compares by reference, but rule set 'value-objects' requires value equality
error TR301: The set accessor of 'Order.Status' is public, but rule set 'entities' allows at most private
error TR302: 'Order.Lines' exposes the mutable collection type 'List<OrderLine>', but rule set 'entities' requires read-only collections
error TR303: 'Money.Currency' has a set accessor, but rule set 'value-objects' requires immutable types
error TR304: 'Order.Customer' holds 'Customer', a type of rule set 'entities', which rule set 'entities' forbids
```

## Install

> **Not on NuGet yet.** The package will be published once the rule set settles. Until then, use
> it from source (below).

```bash
dotnet add package Hlibz.TypeRules.Analyzers
```

It's a development dependency: it runs at build time and adds nothing to your output. Requires the
.NET 8 SDK or later (Visual Studio 2022 17.8+, or a current Rider).

### From source

Reference the analyzer project as an analyzer, not as a library:

```xml
<ItemGroup>
  <ProjectReference Include="path/to/Hlibz.TypeRules.Analyzers/src/Hlibz.TypeRules.Analyzers/Hlibz.TypeRules.Analyzers.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

That brings the diagnostics but not the code fixes. For those too, pack the package locally and
add the output folder as a NuGet source:

```bash
dotnet pack src/Hlibz.TypeRules.Analyzers.Package -c Release -o ./nupkg
```

## Quick start

Add rule sets to your `.editorconfig`:

```ini
[*.cs]
# Endpoints: internal, sealed, in an Endpoints namespace.
typerules.endpoints.match = T:MyApp.Api.IEndpoint
typerules.endpoints.max_accessibility = internal
typerules.endpoints.require_sealed = true
typerules.endpoints.namespace_pattern = **.Endpoints

# Handlers: any construction of the generic interface, each with its own IFooHandler interface.
typerules.handlers.match = T:MyApp.Application.IRequestHandler`2
typerules.handlers.max_accessibility = internal
typerules.handlers.require_sealed = true
typerules.handlers.companion_interface = true

# Entities: state changes only through their own methods, other entities referenced by id.
typerules.entities.match = T:MyApp.Domain.Entity`1
typerules.entities.max_setter_accessibility = private
typerules.entities.readonly_collections = true
typerules.entities.forbid_member_types = entities

# Value objects: immutable, compared by value, created through factory methods.
typerules.value-objects.match = T:MyApp.Domain.IValueObject
typerules.value-objects.require_immutable = true
typerules.value-objects.equality = value
typerules.value-objects.max_constructor_accessibility = private
```

Each block is a **rule set**: `match` says which types it applies to (everything that inherits
from or implements the given types), and the other options say what those types must look like.
Nothing is checked until you configure a rule set, so installing the package changes nothing on
its own.

## Rules

IDs are grouped by what a rule looks at: TR1xx the type's declaration, TR2xx how instances are
created and compared, TR3xx the state a type stores.

| ID | Rule | Option | Code fix |
|---|---|---|---|
| [TR000](docs/rules/TR000.md) | TypeRules configuration is invalid | — | — |
| **Type declaration** | | | |
| [TR101](docs/rules/TR101.md) | Type is more accessible than its rule set allows | `max_accessibility` | Change the access modifier |
| [TR102](docs/rules/TR102.md) | Type must be sealed | `require_sealed` | Add `sealed` |
| [TR103](docs/rules/TR103.md) | Type is declared in the wrong namespace | `namespace_pattern` | — (use the IDE's *Move to namespace*) |
| [TR104](docs/rules/TR104.md) | Type must implement its companion interface (`Foo` → `IFoo`) | `companion_interface` | Generate `IFoo.cs`, or implement an existing `IFoo` |
| **Construction and equality** | | | |
| [TR201](docs/rules/TR201.md) | Constructor is more accessible than its rule set allows | `max_constructor_accessibility` | Change the access modifier |
| [TR202](docs/rules/TR202.md) | Type has the wrong equality semantics: value objects compare by value, entities by identity | `equality` | — |
| **State** | | | |
| [TR301](docs/rules/TR301.md) | Setter is more accessible than its rule set allows | `max_setter_accessibility` | Restrict the accessor, e.g. `private set` |
| [TR302](docs/rules/TR302.md) | Mutable collection (`List<T>`, arrays, ...) is exposed | `readonly_collections` | Expose `IReadOnlyList<T>` & co. |
| [TR303](docs/rules/TR303.md) | Type must be immutable: readonly fields, no setters, no mutable collections, readonly structs | `require_immutable` | Add `readonly`, remove the setter or make it `init` |
| [TR304](docs/rules/TR304.md) | Type holds a reference to a forbidden type, e.g. one aggregate root holding another | `forbid_member_types` | — |

All rules are warnings by default. With `TreatWarningsAsErrors`, they fail the build. Change a
rule's severity the usual way:

```ini
dotnet_diagnostic.TR102.severity = error
```

### Code fixes

A code fix is only offered when applying it can't break the build. Otherwise the diagnostic stays
and the fix is left out, rather than trading one error for another. For example:

- **TR102** doesn't seal a class that something derives from.
- **TR301** doesn't restrict the setter of a `required` property or an interface implementation.
- **TR302** compiles the change first, in every file that uses the member, and only offers it if
  that produces no errors. If a caller does `order.Lines.Add(line)`, there's no fix: that call is
  what needs to move into a method on `Order`. **TR201** and **TR303** check their fixes the same
  way, so a constructor something still calls isn't made `private`, and a field something still
  assigns isn't made `readonly`.

Every fix supports **Fix All** in a document, project or solution, including the TR104 fix, which
creates new files.

## Configuration

Keys are named `typerules.<rule set>.<option>`. The rule-set name is yours to choose: letters,
digits, `_` and `-`.

| Option | Value | Meaning |
|---|---|---|
| `match` | One or more types, separated by `\|` or `,` | The rule set applies to every class, struct and record that inherits from or implements any of them, directly or indirectly |
| `max_accessibility` | `public`, `protected_internal`, `internal`, `protected`, `private_protected` or `private` | TR101: matched types can't be more accessible than this |
| `require_sealed` | `true` or `false` | TR102: matched non-abstract classes must be sealed |
| `companion_interface` | `true` or `false` | TR104: matched non-abstract types must implement an `I<TypeName>` interface, declared next to them, that extends a matched interface. Every `match` type must then be an interface |
| `max_setter_accessibility` | Same values as `max_accessibility` | TR301: `set` and `init` accessors of properties the matched types declare can't be more accessible than this |
| `allow_init` | `true` or `false` | TR301: exempt `init` accessors. Only valid with `max_setter_accessibility` |
| `readonly_collections` | `true` or `false` | TR302: properties and fields visible outside the matched types can't have a mutable collection type |
| `namespace_pattern` | Dot-separated names; `*` is one segment, `**` any number; alternatives separated by `\|` | TR103: matched types must be declared in a namespace matching one alternative, e.g. `**.Database.Configurations` |
| `forbid_member_types` | Rule-set names, separated by `\|` or `,`; may name the rule set itself | TR304: fields and auto-properties of matched types can't hold types of those rule sets, directly or in collections. A rule set named here needs only `match` |
| `allow_self_references` | `true` (default) or `false` | TR304: whether a type may still hold its own type (`Category.Parent`). Only valid with `forbid_member_types` |
| `require_immutable` | `true` or `false` | TR303: matched types' instance fields must be readonly, their properties can't have `set` accessors (`init` is fine), their fields and auto-properties can't store mutable collections, and structs must be `readonly struct`s |
| `equality` | `value` or `identity` | TR202: `value` requires a record, a struct, or an `Equals(object)` override (inherited ones count); `identity` forbids records and structs |
| `max_constructor_accessibility` | Same values as `max_accessibility` | TR201: constructors of matched non-abstract types, including implicit and primary ones, can't be more accessible than this |

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
- **Abstract classes are checked by TR101, TR103, TR202 and TR303** (an abstract endpoint base is
  still an endpoint) but not by TR102, TR104 or TR201: they can't be sealed, nothing resolves them
  from DI, and their constructors exist for derived types.
- **Effective accessibility** (TR101, TR201, TR301, TR302): a `public` member of an `internal`
  class is internal.
- **Members** (TR301, TR302, TR303, TR304) are checked on the type that declares them.
  Overrides and explicit interface implementations are skipped, since what they override or
  implement dictates their shape.
- **Partial types** are reported once.
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
| Type name suffix by base type (every `IEndpoint` ends in `Endpoint`) | Built-in [CA1710](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1710), configured as shown below |
| Seal every internal type that has no subtypes | Built-in [CA1852](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1852). It's off in assemblies with `InternalsVisibleTo` unless you set `dotnet_code_quality.CA1852.ignore_internalsvisibleto = true` |
| Banned APIs (`DateTime.Now`, ...) | [RS0030](https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.BannedApiAnalyzers/BannedApiAnalyzers.Help.md) from Microsoft.CodeAnalysis.BannedApiAnalyzers |
| Layer and namespace dependencies | [NsDepCop](https://github.com/realvizu/NsDepCop), or ArchUnitNET/NetArchTest in tests |
| EF Core model conventions (naming, precision, cascade deletes, ...), and a check of the navigations EF actually maps between aggregates, complementing TR304 | [Hlibz.EntityFrameworkCore.ModelRules](https://github.com/hlib-zinchenko/Hlibz.EntityFrameworkCore.ModelRules) |

To require the `Endpoint`/`Handler` suffixes for the rule sets in the quick start, add:

```ini
dotnet_code_quality.CA1710.api_surface = all
dotnet_code_quality.CA1710.additional_required_suffixes = T:MyApp.Api.IEndpoint->Endpoint|T:MyApp.Application.IRequestHandler`2->Handler
```

## Versioning

Rule IDs are permanent once released: never renumbered or reused. A rule may report more in a minor or patch
release when it was missing cases. New rules only report once you configure them, so upgrading
never adds diagnostics to a codebase that didn't opt in.

## Contributing

```bash
dotnet build
dotnet test --project tests/Hlibz.TypeRules.Analyzers.Tests
```

`samples/Hlibz.TypeRules.Analyzers.Sample` is a small project configured by its own
`.editorconfig`, with one violations file per rule set. It must build clean, and built with its
violations it must report exactly the diagnostics in `Violations/expected-diagnostics.txt`:

```bash
samples/Hlibz.TypeRules.Analyzers.Sample/verify-violations.sh
```

CI checks both. A new rule needs a test class, a page in `docs/rules/`, a row in the tables above,
and a case in the sample's `Violations/` with its line in `expected-diagnostics.txt`.

## License

[MIT](LICENSE)
