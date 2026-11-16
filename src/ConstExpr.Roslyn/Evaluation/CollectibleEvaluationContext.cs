using System.IO;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ConstExpr.Roslyn.Evaluation;

#pragma warning disable RS1035 // Evaluation must load user assemblies in an unloadable runtime context.
internal sealed class CollectibleEvaluationContext : IDisposable
{
	private static readonly Lazy<byte[]> _adapterImage = new(CreateAdapter);
	private readonly object _bootstrap;
	private readonly object _context;
	private readonly MethodInfo _loadFromStream;
	private readonly MethodInfo _unload;

	internal CollectibleEvaluationContext(string name, Func<AssemblyName, Assembly?> resolve)
	{
		Type contextType = GetContextType();
		ConstructorInfo constructor = contextType.GetConstructor([typeof(string), typeof(bool)])!;
		_loadFromStream = contextType.GetMethod("LoadFromStream", [typeof(Stream)])!;
		_unload = contextType.GetMethod("Unload", Type.EmptyTypes)!;
		_bootstrap = constructor.Invoke([name + ".bootstrap", true]);
		try
		{
			using MemoryStream stream = new(_adapterImage.Value, writable: false);
			Assembly adapter = (Assembly)_loadFromStream.Invoke(_bootstrap, [stream])!;
			Type implementation = adapter.GetType("ConstExpr.Runtime.EvaluationContext", throwOnError: true)!;
			_context = Activator.CreateInstance(implementation, [name, resolve])!;
		}
		catch
		{
			_unload.Invoke(_bootstrap, null);
			throw;
		}
	}

	internal Assembly Load(byte[] image)
	{
		using MemoryStream stream = new(image, writable: false);
		return (Assembly)_loadFromStream.Invoke(_context, [stream])!;
	}

	public void Dispose()
	{
		try
		{
			_unload.Invoke(_context, null);
		}
		finally
		{
			_unload.Invoke(_bootstrap, null);
		}
	}

	private static Type GetContextType()
	{
		Type? type = Type.GetType("System.Runtime.Loader.AssemblyLoadContext, System.Runtime.Loader");
		if (type?.GetConstructor([typeof(string), typeof(bool)]) is null)
		{
			throw new InvalidOperationException("ConstExpr evaluation requires a compiler host with collectible assembly loading, such as the .NET 10 SDK.");
		}
		return type;
	}

	private static byte[] CreateAdapter()
	{
		// The host API is absent from netstandard2.0. Compile its small, typed override against
		// the actual host, and cache only bytes; neither the adapter nor its contexts stay loaded.
		const string source = """
			using System;
			using System.Reflection;
			using System.Runtime.Loader;
			namespace ConstExpr.Runtime
			{
				public sealed class EvaluationContext : AssemblyLoadContext
				{
					private readonly Func<AssemblyName, Assembly> _resolve;
					public EvaluationContext(string name, Func<AssemblyName, Assembly> resolve) : base(name, true) { _resolve = resolve; }
					protected override Assembly Load(AssemblyName name) => _resolve(name);
				}
			}
			""";
		MetadataReference[] references = new[] { typeof(object).Assembly.Location, GetContextType().Assembly.Location }
			.Distinct(StringComparer.Ordinal).Select(path => MetadataReference.CreateFromFile(path)).ToArray();
		CSharpCompilation compilation = CSharpCompilation.Create("ConstExpr.EvaluationContext", [CSharpSyntaxTree.ParseText(source)], references,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
		using MemoryStream stream = new();
		Microsoft.CodeAnalysis.Emit.EmitResult result = compilation.Emit(stream);
		if (!result.Success)
		{
			throw new InvalidOperationException(string.Join("\n", result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));
		}
		return stream.ToArray();
	}
}
#pragma warning restore RS1035
