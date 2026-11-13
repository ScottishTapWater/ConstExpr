# ConstExpr

A C# analyser and source generator library targeting .NET Standard 2.0.

The initial project contains an empty `IIncrementalGenerator.Initialize` entry point. It does not generate source or report diagnostics yet.

## Structure

`src/ConstExpr/External` contains public entry points in `ConstExpr.External`. Internal implementation belongs in `src/ConstExpr/Internal`, under `ConstExpr.Internal`. ArchonAnalysers enforces namespace accessibility through `.archonanalysers`.

The generator uses Roslyn 5.0 and requires a compatible compiler host, such as the .NET 10 SDK. Its assembly targets `netstandard2.0` and is packaged under `analyzers/dotnet/cs`, with no runtime library or Roslyn package dependencies.

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
