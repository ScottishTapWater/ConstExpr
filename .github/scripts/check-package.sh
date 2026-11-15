#!/usr/bin/env bash
set -euo pipefail

version="${1:?Usage: check-package.sh VERSION}"
repo_root="$(git rev-parse --show-toplevel)"
package_dir="$repo_root/artifacts/packages"
package="$package_dir/ConstExpr.$version.nupkg"
scratch_dir="$(mktemp -d)"
trap 'rm -rf "$scratch_dir"' EXIT

unzip -Z1 "$package" > "$scratch_dir/contents.txt"
cat > "$scratch_dir/expected-assemblies.txt" <<'EOF'
analyzers/dotnet/cs/ConstExpr.Core.dll
analyzers/dotnet/cs/ConstExpr.Roslyn.dll
analyzers/dotnet/cs/ConstExpr.dll
analyzers/dotnet/cs/Microsoft.Bcl.AsyncInterfaces.dll
analyzers/dotnet/cs/System.Buffers.dll
analyzers/dotnet/cs/System.IO.Pipelines.dll
analyzers/dotnet/cs/System.Memory.dll
analyzers/dotnet/cs/System.Numerics.Vectors.dll
analyzers/dotnet/cs/System.Runtime.CompilerServices.Unsafe.dll
analyzers/dotnet/cs/System.Text.Encodings.Web.dll
analyzers/dotnet/cs/System.Text.Json.dll
analyzers/dotnet/cs/System.Threading.Tasks.Extensions.dll
lib/netstandard2.0/ConstExpr.dll
EOF
grep '\.dll$' "$scratch_dir/contents.txt" | sort > "$scratch_dir/actual-assemblies.txt"
diff -u "$scratch_dir/expected-assemblies.txt" "$scratch_dir/actual-assemblies.txt"
grep -Fx 'README.md' "$scratch_dir/contents.txt"
if grep -Eqi 'ArchonAnalysers|Microsoft\.CodeAnalysis' "$scratch_dir/contents.txt"; then
  echo 'Build-time dependencies must not be bundled in the package.' >&2
  exit 1
fi
unzip -p "$package" ConstExpr.nuspec > "$scratch_dir/ConstExpr.nuspec"
if grep '<dependency ' "$scratch_dir/ConstExpr.nuspec" | grep -v 'id="NETStandard.Library"'; then
  echo 'The package must not expose build-time package dependencies.' >&2
  exit 1
fi
