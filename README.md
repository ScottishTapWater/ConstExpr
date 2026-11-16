# ConstExpr

A C# analyser and source generator library targeting .NET Standard 2.0.

The generator evaluates static `[ConstExpr]` methods during compilation and generates interceptors that return their precomputed values. Results use System.Text.Json. Collections, ordinary DTOs, and null results are supported. Consumers must enable interception for the `ConstExpr.Generated` namespace.

Arguments can be C# expressions, including constructors, collection expressions, queries, static factories, fields, properties, lambdas and awaited inputs. Input objects do not need to be serialisable. Expressions execute against the build environment; ConstExpr does not prove purity or determinism. Arguments requiring caller runtime locals, parameters, an instance or unresolved generic types report warning `CONSTEXPR002` and leave the call running normally at runtime. Following local assignments is not supported.

Return and parameter types must be accessible to generated interceptors. Calls whose signatures contain private or file-local types report warning `CONSTEXPR005` and run normally at runtime.

Successfully precomputed calls with nonconstant arguments report warning `CONSTEXPR004`: argument expressions still execute at runtime, but their values are ignored, even if those values change. Move argument construction inside the `[ConstExpr]` method to avoid that runtime work.

Constant scalar arguments share one reconstructed result per method and argument combination. Other calls share one reconstructed result per call site. ConstExpr does not compare arbitrary input object graphs to merge results between call sites.

Compiled and interpreted expression-tree calls are intercepted with the supported compiler. Method-group delegates, dynamically bound calls to marked methods, reflection invocations and managed function pointers execute the original method. Method-group conversions and taking a method's address report warning `CONSTEXPR003`.

Shared references and cycles in STJ-supported object graphs are preserved using `ReferenceHandler.Preserve`.

Type-level `[JsonConverter]` attributes are honoured during build-time serialisation and runtime reconstruction.

Expression evaluation adds uniquely marked static local functions to a build-only compilation. Nongeneric callers retain their lexical scope. Independent expressions in generic callers use a separate nongeneric scope without guessing type arguments. Extraction preserves the original expression text and source context. Before execution, it verifies selected members, conversions, typed constants and compiler-supplied arguments against the original compiler binding. A binding mismatch reports `CONSTEXPR001`.

Evaluation prepares and verifies this temporary compilation before emission and execution inside a collectible load context. Its serializer and private dependencies share that context, and only serialised results are retained after evaluation. Contexts are unloaded after each generator run. Scalar evaluations retain their existing shared build-time state and cache; separate expression call sites retain independent build-time state. The package's MSBuild target supplies resolved runtime assembly paths for project and NuGet dependencies. Methods must be compilable before generated source is added. Evaluation helpers do not ship in the application. Execution still occurs in the compiler process; process isolation is outside the current implementation.

## Structure

| Project | Responsibility |
| --- | --- |
| `ConstExpr` | The method-only `[ConstExpr]` attribute and application-facing contracts. |
| `ConstExpr.Core` | Roslyn-independent method evaluation and serialisation using System.Text.Json. |
| `ConstExpr.Roslyn` | Compiler integration and interceptor generation in `ConstExpr.Roslyn.Generators`. |
| `ConstExpr.Core.Tests` | TUnit tests for evaluation and serialisation. |
| `ConstExpr.Roslyn.Tests` | Compiler controls, pipeline tests, call-site theories, and source-edit tests. |
| `ConstExpr.IntegrationTests` | A consumer build and run using a temporary NuGet package. |

Roslyn references Core and contracts. Core references contracts. ArchonAnalysers enforces this direction and forbids `Microsoft.CodeAnalysis*` assembly references from Core and contracts. It is a private development dependency in every project and is excluded from the consumer package.

The three production projects target `netstandard2.0`. Test projects target `net10.0`. One `ConstExpr` package contains the contracts assembly under `lib/netstandard2.0` and all three production assemblies under `analyzers/dotnet/cs` for generator dependency resolution. System.Text.Json and its transitive dependencies are bundled beside the generator, and a public System.Text.Json package dependency supplies runtime deserialisation. Core and Roslyn are not runtime references for applications. No Archon, Roslyn, or test package dependencies are exposed to consumers.

The generator uses Roslyn 5.0. Evaluation requires a compiler host with collectible assembly loading, such as the .NET 10 SDK. The analyser and Core assemblies remain `netstandard2.0`.

## Build and check the package

```sh
dotnet restore src/ConstExpr.slnx
dotnet build src/ConstExpr.slnx -c Release --no-restore
dotnet format src/ConstExpr.slnx --verify-no-changes --no-restore
dotnet pack src/ConstExpr.slnx -c Release --no-build --no-restore -o artifacts/packages
bash .github/scripts/check-package.sh 0.1.0
```

The package check validates the NuGet layout and dependency metadata. The integration test packs ConstExpr, then builds and runs a temporary consumer using that package.

## Tests

```sh
dotnet test --solution src/ConstExpr.slnx -c Release --no-build --no-restore --report-trx --results-directory "$PWD/artifacts/test-results"
```

## Releases

CI builds, checks formatting, and validates the NuGet package on pull requests. CD runs on changes to `main`, calculates a version from conventional commits, and publishes to NuGet.org and GitHub Packages before creating a GitHub release. `v0.0.0` marks the initial repository commit for version calculation. CD also supports manual dispatch.

NuGet publishing uses `NuGet/login@v1` with OIDC. The repository variable `NUGET_USER` is `hughesjs`. Publishing requires a [NuGet trusted publishing policy](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing) with these values:

| Field | Value |
| --- | --- |
| Repository owner | `ScottishTapWater` |
| Repository | `ConstExpr` |
| Workflow file | `cd.yml` |
| Environment | Leave empty |
| Package scope | `ConstExpr`, including new packages |

The publishing step will fail until that policy exists.
