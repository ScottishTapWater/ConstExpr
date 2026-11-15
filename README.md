# ConstExpr

A C# analyser and source generator library targeting .NET Standard 2.0.

The generator entry point is empty. `[ConstExpr]` is available, and Core evaluation and serialisation entry points throw `NotImplementedException`. The active test suite describes the intended behaviour and is deliberately red until implementation.

## Structure

| Project | Responsibility |
| --- | --- |
| `ConstExpr` | The method-only `[ConstExpr]` attribute and application-facing contracts. |
| `ConstExpr.Core` | Roslyn-independent evaluation and serialisation stubs. |
| `ConstExpr.Roslyn` | Compiler integration and the empty generator entry point in `ConstExpr.Roslyn.Generators`. |
| `ConstExpr.Core.Tests` | TUnit tests for evaluation and serialisation. |
| `ConstExpr.Roslyn.Tests` | Compiler controls, pipeline tests, call-site theories, and source-edit tests. |

Roslyn references Core and contracts. Core references contracts. ArchonAnalysers enforces this direction and forbids `Microsoft.CodeAnalysis*` assembly references from Core and contracts. It is a private development dependency in every project and is excluded from the consumer package.

The three production projects target `netstandard2.0`. Test projects target `net10.0`. One `ConstExpr` package contains the contracts assembly under `lib/netstandard2.0` and all three production assemblies under `analyzers/dotnet/cs` for generator dependency resolution. Core and Roslyn are not runtime references for applications. No Archon, Roslyn, or test package dependencies are exposed to consumers.

The generator uses Roslyn 5.0 and requires a compatible compiler host, such as the .NET 10 SDK.

## Build and check the package

```sh
dotnet restore src/ConstExpr.slnx
dotnet build src/ConstExpr.slnx -c Release --no-restore
dotnet format src/ConstExpr.slnx --verify-no-changes --no-restore
dotnet pack src/ConstExpr.slnx -c Release --no-build --no-restore -o artifacts/packages
bash .github/scripts/check-package.sh 0.1.0
```

The package check validates the NuGet layout and builds a consumer that loads the packaged generator.

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
