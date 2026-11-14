#!/usr/bin/env bash
set -euo pipefail

version="${1:?Usage: check-package.sh VERSION}"
repo_root="$(git rev-parse --show-toplevel)"
package_dir="$repo_root/artifacts/packages"
package="$package_dir/ConstExpr.$version.nupkg"
consumer_dir="$(mktemp -d)"
trap 'rm -rf "$consumer_dir"' EXIT

unzip -Z1 "$package" > "$consumer_dir/contents.txt"
cat > "$consumer_dir/expected-assemblies.txt" <<'EOF'
analyzers/dotnet/cs/ConstExpr.Core.dll
analyzers/dotnet/cs/ConstExpr.Roslyn.dll
analyzers/dotnet/cs/ConstExpr.dll
lib/netstandard2.0/ConstExpr.dll
EOF
grep '\.dll$' "$consumer_dir/contents.txt" | sort > "$consumer_dir/actual-assemblies.txt"
diff -u "$consumer_dir/expected-assemblies.txt" "$consumer_dir/actual-assemblies.txt"
grep -Fx 'README.md' "$consumer_dir/contents.txt"
if grep -Eqi 'ArchonAnalysers|Microsoft\.CodeAnalysis' "$consumer_dir/contents.txt"; then
  echo 'Build-time dependencies must not be bundled in the package.' >&2
  exit 1
fi
unzip -p "$package" ConstExpr.nuspec > "$consumer_dir/ConstExpr.nuspec"
if grep '<dependency ' "$consumer_dir/ConstExpr.nuspec" | grep -v 'id="NETStandard.Library"'; then
  echo 'The package must not expose build-time package dependencies.' >&2
  exit 1
fi

mkdir "$consumer_dir/application" "$consumer_dir/driver"
cat > "$consumer_dir/application/Consumer.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="ConstExpr" Version="$version" />
  </ItemGroup>
</Project>
EOF
cat > "$consumer_dir/application/Example.cs" <<'EOF'
namespace Consumer;

public sealed class Example;
EOF
dotnet restore "$consumer_dir/application/Consumer.csproj" --source "$package_dir" --source https://api.nuget.org/v3/index.json --packages "$consumer_dir/packages"
dotnet build "$consumer_dir/application/Consumer.csproj" -c Release --no-restore
if grep -Eqi 'ArchonAnalysers|Microsoft\.CodeAnalysis|ConstExpr\.Core|ConstExpr\.Roslyn' "$consumer_dir/application/bin/Release/net10.0/Consumer.deps.json"; then
  echo 'Build-time dependencies must not enter the application runtime dependency graph.' >&2
  exit 1
fi
if grep -Eqi 'ArchonAnalysers|Microsoft\.CodeAnalysis' "$consumer_dir/application/obj/project.assets.json"; then
  echo 'Build-time packages must not be restored for consumers.' >&2
  exit 1
fi

cat > "$consumer_dir/driver/Driver.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="5.0.0" />
  </ItemGroup>
</Project>
EOF
cat > "$consumer_dir/driver/Program.cs" <<'EOF'
using System;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Consumer.Internal;

internal static class Program
{
	private static void Main(string[] args)
	{
		Assembly assembly = Assembly.LoadFrom(args[0]);
		Type generatorType = assembly.GetTypes().Single(type => type.GetCustomAttribute<GeneratorAttribute>() is not null);
		IIncrementalGenerator generator = (IIncrementalGenerator)Activator.CreateInstance(generatorType);
		GeneratorDriver driver = CSharpGeneratorDriver.Create(generator.AsSourceGenerator());
		GeneratorDriverRunResult result = driver.RunGenerators(CSharpCompilation.Create("Consumer")).GetRunResult();
		if (result.Results.Single().Exception is not null || result.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
		{
			throw new InvalidOperationException("The packaged generator failed to initialise.");
		}
		Console.WriteLine($"Loaded and ran {generatorType.FullName} from the NuGet package.");
	}
}
EOF

dotnet restore "$consumer_dir/driver/Driver.csproj" --source https://api.nuget.org/v3/index.json --packages "$consumer_dir/packages"
dotnet build "$consumer_dir/driver/Driver.csproj" -c Release --no-restore
unzip -j "$package" 'analyzers/dotnet/cs/*.dll' -d "$consumer_dir/assemblies"
dotnet run --project "$consumer_dir/driver/Driver.csproj" -c Release --no-build --no-restore -- "$consumer_dir/assemblies/ConstExpr.Roslyn.dll"
