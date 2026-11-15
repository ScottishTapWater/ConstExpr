using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using Microsoft.CodeAnalysis;

namespace ConstExpr.Roslyn.Evaluation;

#pragma warning disable RS1035 // Evaluation must load the user's runtime assemblies.
internal sealed class EvaluationAssemblyLoader : IDisposable
{
	private readonly ILookup<string, string> _paths;
	private readonly ConcurrentDictionary<Assembly, byte> _owned = new();
	private readonly ConcurrentDictionary<string, Lazy<Assembly?>> _dependencies = new(StringComparer.OrdinalIgnoreCase);
	private bool _subscribed;

	internal EvaluationAssemblyLoader(Compilation compilation, IEnumerable<string> runtimePaths)
	{
		_paths = runtimePaths.Concat(compilation.References.OfType<PortableExecutableReference>()
			.Select(reference => reference.FilePath).OfType<string>())
			.Distinct(StringComparer.Ordinal)
			.ToLookup(path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase);
	}

	internal Assembly Load(byte[] image)
	{
		if (!_subscribed)
		{
			AppDomain.CurrentDomain.AssemblyResolve += Resolve;
			_subscribed = true;
		}
		return LoadImage(image);
	}

	private Assembly LoadImage(byte[] image)
	{
		Assembly assembly = Assembly.Load(image);
		_owned.TryAdd(assembly, 0);
		return assembly;
	}

	private Assembly? Resolve(object? sender, ResolveEventArgs args)
	{
		if (args.RequestingAssembly is null || !_owned.ContainsKey(args.RequestingAssembly))
		{
			return null;
		}
		return _dependencies.GetOrAdd(args.Name, name => new(() => LoadDependency(name))).Value;
	}

	private Assembly? LoadDependency(string name)
	{
		AssemblyName requested = new(name);
		foreach (string path in _paths[requested.Name!])
		{
			if (string.Equals(AssemblyName.GetAssemblyName(path).FullName, requested.FullName, StringComparison.OrdinalIgnoreCase))
			{
				return LoadImage(File.ReadAllBytes(path));
			}
		}
		return null;
	}

	public void Dispose()
	{
		if (_subscribed)
		{
			AppDomain.CurrentDomain.AssemblyResolve -= Resolve;
			_subscribed = false;
		}
	}
}
#pragma warning restore RS1035
