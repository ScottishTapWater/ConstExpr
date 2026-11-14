using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;

namespace ConstExpr.Roslyn.Tests.Infrastructure;

internal sealed class LoadedFixture : IDisposable
{
	private readonly AssemblyLoadContext _context;
	private readonly Assembly _assembly;
	private readonly string _entryType;

	internal LoadedFixture(Compilation compilation, string entryType = "Fixture.Counter")
	{
		_entryType = entryType;
		using MemoryStream stream = new();
		Microsoft.CodeAnalysis.Emit.EmitResult emitted = compilation.Emit(stream);
		CompilationFixture.ThrowOnErrors(emitted.Diagnostics);
		stream.Position = 0;
		_context = new(compilation.AssemblyName, isCollectible: true);
		_assembly = _context.LoadFromStream(stream);
	}

	internal int Calls => CallsFor("Fixture.Counter");

	internal int CallsFor(string typeName) => (int)_assembly.GetType(typeName, throwOnError: true)!.GetProperty("Calls")!.GetValue(null)!;

	internal async Task<int[]> Run()
	{
		object result = _assembly.GetType(_entryType, throwOnError: true)!.GetMethod("Run")!.Invoke(null, null)!;
		return result is Task<int[]> task ? await task : (int[])result;
	}

	public void Dispose()
	{
		_context.Unload();
	}
}
