# ConstExpr

A C# analyser and source generator library targeting .NET Standard 2.0.

The generator evaluates static `[ConstExpr]` methods returning `int` during compilation and generates interceptors that return their precomputed values. Methods can be parameterless or accept `int` parameters. Calls with constant arguments are intercepted; calls with any nonconstant argument report warning `CONSTEXPR002` and run normally at runtime. Method-group delegates continue to execute the original method. Consumers must enable interception for the `ConstExpr.Generated` namespace.

Evaluation currently compiles and loads the original compilation inside the compiler process. Methods must be compilable before generated source is added. General result types and isolated execution are outside this initial slice.

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

The three production projects target `netstandard2.0`. Test projects target `net10.0`. One `ConstExpr` package contains the contracts assembly under `lib/netstandard2.0` and all three production assemblies under `analyzers/dotnet/cs` for generator dependency resolution. System.Text.Json and its transitive dependencies are bundled beside the generator. Core, Roslyn, and the bundled dependencies are not runtime references for applications. No Archon, Roslyn, or test package dependencies are exposed to consumers.

The generator uses Roslyn 5.0 and requires a compatible compiler host, such as the .NET 10 SDK.

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
