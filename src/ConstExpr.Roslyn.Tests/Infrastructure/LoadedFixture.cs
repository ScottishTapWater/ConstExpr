using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;

namespace ConstExpr.Roslyn.Tests.Infrastructure;

internal sealed class LoadedFixture : IDisposable
{
	private readonly AssemblyLoadContext _context;
	private readonly Assembly _assembly;

	internal LoadedFixture(Compilation compilation)
	{
		using MemoryStream stream = new();
		Microsoft.CodeAnalysis.Emit.EmitResult emitted = compilation.Emit(stream);
		CompilationFixture.ThrowOnErrors(emitted.Diagnostics);
		stream.Position = 0;
		_context = new(compilation.AssemblyName, isCollectible: true);
		_assembly = _context.LoadFromStream(stream);
	}

	internal async Task<TResult> Run<TResult>(string entrypoint)
	{
		int separator = entrypoint.LastIndexOf('.');
		string typeName = entrypoint[..separator];
		string methodName = entrypoint[(separator + 1)..];
		Type type = _assembly.GetType(typeName, throwOnError: true)!;
		MethodInfo method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, Type.EmptyTypes)
			?? throw new MissingMethodException(typeName, methodName);
		object result = method.Invoke(null, null)!;
		return result is Task<TResult> task ? await task : (TResult)result;
	}

	public void Dispose()
	{
		_context.Unload();
	}
}
