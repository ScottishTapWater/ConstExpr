# ConstExpr

A C# analyser and source generator library targeting .NET Standard 2.0.

The initial project contains an empty `IIncrementalGenerator.Initialize` entry point. It does not generate source or report diagnostics yet.

## Structure

| Project | Responsibility |
| --- | --- |
| `ConstExpr` | Attributes and application-facing contracts. Empty for now. |
| `ConstExpr.Core` | Evaluation, serialisation, and result handling, independent of Roslyn. Empty for now. |
| `ConstExpr.Roslyn` | Compiler integration and the empty generator entry point in `ConstExpr.Roslyn.Generators`. |

Roslyn references Core and contracts. Core references contracts. ArchonAnalysers enforces this direction and forbids `Microsoft.CodeAnalysis*` assembly references from Core and contracts. It is a private development dependency in every project and is excluded from the consumer package.

All three projects target `netstandard2.0`. One `ConstExpr` package contains the contracts assembly under `lib/netstandard2.0` and all three assemblies under `analyzers/dotnet/cs` for generator dependency resolution. Core and Roslyn are not runtime references for applications. No Archon or Roslyn package dependencies are exposed to consumers.

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
