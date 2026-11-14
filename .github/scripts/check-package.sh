#!/usr/bin/env bash
set -euo pipefail

version="${1:?Usage: check-package.sh VERSION}"
repo_root="$(git rev-parse --show-toplevel)"
package_dir="$repo_root/artifacts/packages"
package="$package_dir/ConstExpr.$version.nupkg"
consumer_dir="$(mktemp -d)"
trap 'rm -rf "$consumer_dir"' EXIT

unzip -Z1 "$package" > "$consumer_dir/contents.txt"
grep -Fx 'analyzers/dotnet/cs/ConstExpr.dll' "$consumer_dir/contents.txt"
grep -Fx 'README.md' "$consumer_dir/contents.txt"
if grep -Eq '^(lib|ref)/|^analyzers/.*(Microsoft\.CodeAnalysis|ArchonAnalysers).*\.dll$' "$consumer_dir/contents.txt"; then
  echo 'The package must contain only the ConstExpr analyser assembly, with no runtime assets.' >&2
  exit 1
fi
unzip -p "$package" ConstExpr.nuspec > "$consumer_dir/ConstExpr.nuspec"
if grep -q '<dependency ' "$consumer_dir/ConstExpr.nuspec"; then
  echo 'The analyser package must not expose build-time package dependencies.' >&2
  exit 1
fi

cat > "$consumer_dir/Consumer.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="ConstExpr" Version="$version" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="5.0.0" />
  </ItemGroup>
</Project>
EOF
cat > "$consumer_dir/Consumer.cs" <<'EOF'
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

dotnet restore "$consumer_dir/Consumer.csproj" --source "$package_dir" --source https://api.nuget.org/v3/index.json --packages "$consumer_dir/packages"
dotnet build "$consumer_dir/Consumer.csproj" -c Release --no-restore
unzip -p "$package" analyzers/dotnet/cs/ConstExpr.dll > "$consumer_dir/ConstExpr.dll"
dotnet run --project "$consumer_dir/Consumer.csproj" -c Release --no-build --no-restore -- "$consumer_dir/ConstExpr.dll"
