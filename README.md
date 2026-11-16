# ConstExpr

[![CI](https://img.shields.io/github/actions/workflow/status/ScottishTapWater/ConstExpr/ci.yml?style=for-the-badge&logo=github&label=CI)](https://github.com/ScottishTapWater/ConstExpr/actions/workflows/ci.yml)
[![CD](https://img.shields.io/github/actions/workflow/status/ScottishTapWater/ConstExpr/cd.yml?branch=main&style=for-the-badge&logo=github&label=CD)](https://github.com/ScottishTapWater/ConstExpr/actions/workflows/cd.yml)
[![NuGet](https://img.shields.io/nuget/v/ConstExpr?style=for-the-badge&logo=nuget)](https://www.nuget.org/packages/ConstExpr/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/ConstExpr?style=for-the-badge)](https://www.nuget.org/packages/ConstExpr/)
[![License](https://img.shields.io/github/license/ScottishTapWater/ConstExpr?style=for-the-badge)](LICENSE)

ConstExpr is a Roslyn source generator that executes marked C# methods at build time and makes their precomputed results available to your application.

Mark a static method with `[ConstExpr]`. The generator evaluates supported calls, serialises the results with System.Text.Json, and generates interceptors that return the reconstructed results at runtime.

The computation happens during the build. Deserialisation happens at runtime. ConstExpr does not change C#'s rules for `const`, optional parameter defaults, or attribute arguments.

## Getting started

The supported build environment is the .NET 10 SDK with its Roslyn 5.0 compiler. The library and generator assemblies target .NET Standard 2.0. The package includes the attribute, generator, analysers, and a System.Text.Json runtime dependency.

```sh
dotnet add package ConstExpr
```

Enable interceptors for the generated namespace in your project file:

```xml
<PropertyGroup>
	<InterceptorsNamespaces>$(InterceptorsNamespaces);ConstExpr.Generated</InterceptorsNamespaces>
</PropertyGroup>
```

This program computes the array during the build:

```csharp
using System;
using System.Linq;
using ConstExpr;

public static class Program
{
	[ConstExpr]
	private static int[] Squares(int count) =>
		Enumerable.Range(1, count).Select(value => value * value).ToArray();

	public static void Main()
	{
		int[] squares = Squares(4);
		Console.WriteLine(string.Join(", ", squares));
	}
}
```

Output:

```text
1, 4, 9, 16
```

The call to `Squares(4)` returns the reconstructed array. The application does not run the original method for that call.

## Arguments

Arguments can be expressions that ConstExpr can evaluate without the caller's runtime state. Supported inputs include literals, constructors, collection expressions, LINQ queries, static factories, fields, properties, lambdas, and awaited expressions.

Input objects do not need to be serialisable. Only the result needs to round-trip through System.Text.Json.

```csharp
using System.Collections.Generic;
using System.Linq;
using ConstExpr;

public static class Totals
{
	[ConstExpr]
	public static int Sum(IEnumerable<int> values) => values.Sum();

	public static int Read() => Sum(new[] { 20, 22 });
}
```

`Read()` returns the precomputed value `42`, but the array argument still gets constructed at runtime. ConstExpr reports `CONSTEXPR004` for this call.

### Runtime argument evaluation

Interceptors redirect supported calls to generated code. They do not remove evaluation of the arguments. Argument expressions still execute at runtime, and the interceptor ignores their values, even when those values differ from the build-time values.

To avoid that runtime work, move input construction into the marked method:

```csharp
using System.Linq;
using ConstExpr;

public static class PrecomputedTotals
{
	[ConstExpr]
	public static int Sum() => new[] { 20, 22 }.Sum();
}
```

Arguments that depend on caller locals, parameters, or an instance produce `CONSTEXPR002`. Those calls execute the original method at runtime. ConstExpr accepts local constants but does not follow assignments to runtime locals.

Evaluation uses the build environment. ConstExpr does not prove that methods are pure or deterministic. Files, environment variables, static state, and other inputs can affect the generated result.

## Generic methods

Generic methods and methods inside generic types are supported when all type arguments are concrete at the call site. Type arguments can be explicit or inferred. C# checks generic constraints as usual.

```csharp
using ConstExpr;

public static class Values
{
	[ConstExpr]
	public static T Identity<T>(T value) => value;

	public static int Inferred() => Identity(42);
	public static int Explicit() => Identity<int>(42);
	public static T FromCaller<T>(T value) => Identity<T>(value);
}
```

`Inferred()` and `Explicit()` use the same closed method, `Identity<int>`, and the same cached result. `FromCaller<T>()` produces `CONSTEXPR002` and runs normally because its type and value depend on the caller.

Calling `FromCaller<int>()` elsewhere does not specialise the call inside its generic body. Even a parameterless marked call such as `Create<T>()` needs a concrete type argument at its own call site.

## Results and shared instances

Results follow System.Text.Json's serialisation rules. Supported results include scalar values, arrays, collections, ordinary DTOs, and `null`. Private state needs an explicit serialisation contract, such as a custom converter.

ConstExpr uses `ReferenceHandler.Preserve` to retain shared references and cycles in object graphs supported by System.Text.Json. Types outside that support still need a converter or a different result representation.

Results can be mutable. Calls return shared reconstructed instances according to these rules:

| Call | Shared result |
| --- | --- |
| No arguments, or constant scalar arguments | One result per closed method and argument combination, across call sites. |
| Other argument expressions | One result per call site, reused each time that call executes. |
| Different generic type arguments | Separate cached results. |

Mutations are visible through later calls that share the same result. ConstExpr does not return a fresh instance on each call or compare arbitrary input object graphs to merge results.

Scalar evaluations share build-time static state. Separate expression call sites use independent build-time state. Static fields on a generic containing type follow C#'s normal per-closed-type behaviour.

### Custom converters

Type-level `[JsonConverter]` attributes apply during both build-time serialisation and runtime deserialisation. For example, this converter preserves a value stored in a private field:

```csharp
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

[JsonConverter(typeof(SecretConverter))]
public sealed class Secret
{
	private readonly int value;

	public Secret(int value) { this.value = value; }
	public int ReadValue() => value;
}

public sealed class SecretConverter : JsonConverter<Secret>
{
	public override Secret Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
		new(reader.GetInt32());

	public override void Write(Utf8JsonWriter writer, Secret value, JsonSerializerOptions options) =>
		writer.WriteNumberValue(value.ReadValue());
}
```

A marked method can return `new Secret(42)`. Its reconstructed result returns `42` from `ReadValue()`.

## Interception limits

ConstExpr intercepts individual calls in the compilation that runs the generator. It does not replace the original method globally or modify already compiled assemblies.

| Call form | Behaviour |
| --- | --- |
| Supported direct method call | Returns the precomputed result. |
| Call inside a compiled or interpreted expression tree | Returns the precomputed result with the supported compiler. |
| Method-group delegate, such as `Func<int> read = Factory.Create` | Executes the original method. The conversion reports `CONSTEXPR003`. |
| Managed function pointer | Executes the original method. Taking the address reports `CONSTEXPR003`. |
| Dynamically bound call or reflection invocation | Executes the original method. ConstExpr does not diagnose these calls. |

Marked methods must be static ordinary methods with a return value. Parameters declared with `ref`, `out`, or `in` are unsupported. Ref returns, pointer signatures, and ref-like signature types are also unsupported. Containing classes do not need to be `partial`.

Return and parameter types must be accessible to generated interceptors. Private or file-local signature types produce `CONSTEXPR005`, and the call runs normally. The marked method itself can be private.

Methods must compile before other generators add source. ConstExpr resolves runtime dependencies from project and NuGet references, but does not run other generators before evaluation.

Build-time code executes inside the compiler process with the build's permissions. Collectible load contexts isolate evaluation assemblies and allow them to unload. They do not provide a process sandbox.

## Diagnostics

| Code | Severity | Meaning |
| --- | --- | --- |
| `CONSTEXPR001` | Error | Evaluation, serialisation, or verification of the extracted expression's compiler binding failed. |
| `CONSTEXPR002` | Warning | The call needs caller runtime state or unresolved type arguments. The original call remains. |
| `CONSTEXPR003` | Warning | A method-group delegate or function pointer bypasses compile-time evaluation. |
| `CONSTEXPR004` | Warning | A precomputed call still evaluates nonconstant arguments at runtime and ignores their values. |
| `CONSTEXPR005` | Warning | Signature types are inaccessible to generated interceptors. The original call remains. |

## Development

The SDK version is pinned in [global.json](global.json). The solution uses TUnit. Integration tests pack ConstExpr, then build and run consumers against that package.

```sh
dotnet restore src/ConstExpr.slnx
dotnet build src/ConstExpr.slnx -c Release --no-restore
dotnet test --solution src/ConstExpr.slnx -c Release --no-build --no-restore
dotnet format src/ConstExpr.slnx --verify-no-changes --no-restore
```

The package layout check uses the same version as the pack command:

```sh
dotnet pack src/ConstExpr.slnx -c Release --no-build --no-restore -o artifacts/packages -p:PackageVersion=0.0.0-local
bash .github/scripts/check-package.sh 0.0.0-local
```

| Project | Responsibility |
| --- | --- |
| `ConstExpr` | The `[ConstExpr]` attribute and application-facing contracts. |
| `ConstExpr.Core` | Roslyn-independent evaluation and System.Text.Json serialisation. |
| `ConstExpr.Roslyn` | Compiler integration, diagnostics, and interceptor generation. |

ArchonAnalysers enforces the dependency boundaries. It is a private development dependency and is excluded from the consumer package. Core and Roslyn ship as generator dependencies, not application runtime references.

## Licence

[MIT](LICENSE).
