using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using ConstExpr.Core.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace ConstExpr.Roslyn.Evaluation;

#pragma warning disable RS1035 // Evaluation must load the user's runtime assemblies.
internal sealed class EvaluationAssemblyLoader : IDisposable
{
	private readonly ILookup<string, string> _paths;
	private readonly HashSet<string> _referencePaths;
	private readonly string _contextName;
	private HashSet<string>? _frameworkNames;
	private readonly string _serializerPath = typeof(ResultSerializer).Assembly.Location;
	private readonly ConcurrentDictionary<string, Lazy<Assembly?>> _dependencies = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<string, Lazy<Assembly>> _loaded = new(StringComparer.OrdinalIgnoreCase);
	private CollectibleEvaluationContext? _context;
	private object? _serializer;
	private MethodInfo? _serialize;

	internal EvaluationAssemblyLoader(Compilation compilation, IEnumerable<string> runtimePaths)
	{
		PortableExecutableReference[] references = compilation.References.OfType<PortableExecutableReference>().ToArray();
		string[] paths = runtimePaths.Concat(references.Select(reference => reference.FilePath).OfType<string>())
			.Distinct(StringComparer.Ordinal).Where(File.Exists).ToArray();
		_paths = paths.ToLookup(path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase);
		_referencePaths = new(references.Where(reference => compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol assembly
			&& assembly.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == "System.Runtime.CompilerServices.ReferenceAssemblyAttribute"))
			.Select(reference => reference.FilePath).OfType<string>(), StringComparer.Ordinal);
		_contextName = compilation.AssemblyName + ".ConstExpr." + Guid.NewGuid().ToString("N");
	}

	internal Assembly Load(CSharpCompilation compilation, CancellationToken cancellationToken)
	{
		using MemoryStream stream = new();
		EmitResult result = compilation.Emit(stream, cancellationToken: cancellationToken);
		if (!result.Success)
		{
			throw new InvalidOperationException(string.Join("\n", result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));
		}
		EnsureContext();
		return _context!.Load(stream.ToArray());
	}

	internal string Serialize(object? value, Type type)
	{
		return (string)_serialize!.Invoke(_serializer, [value, type])!;
	}

	internal Assembly LoadRuntimeAssembly(AssemblyName name) => _context!.Load(name);

	private void EnsureContext()
	{
		if (_context is not null)
		{
			return;
		}
		_frameworkNames = new(Directory.GetFiles(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "*.dll")
			.Select(Path.GetFileNameWithoutExtension), StringComparer.OrdinalIgnoreCase);
		_context = new CollectibleEvaluationContext(_contextName, Resolve);
		string jsonPath = ResultSerializer.JsonAssembly.Location;
		// JSON reflection caches must live with the evaluated types, not in the compiler host.
		LoadPrivate(jsonPath);
		Assembly core = LoadPrivate(_serializerPath);
		Type serializerType = core.GetType(typeof(ResultSerializer).FullName!, throwOnError: true)!;
		_serializer = Activator.CreateInstance(serializerType, nonPublic: true);
		_serialize = serializerType.GetMethod(nameof(ResultSerializer.Serialize), [typeof(object), typeof(Type)])!;
	}

	private Assembly LoadPrivate(string path)
	{
		AssemblyName name = AssemblyName.GetAssemblyName(path);
		return _loaded.GetOrAdd(name.FullName, _ => new(() => _context!.Load(File.ReadAllBytes(path)))).Value;
	}

	private Assembly? Resolve(AssemblyName requested)
	{
		if (_frameworkNames!.Contains(requested.Name!))
		{
			return null;
		}
		return _dependencies.GetOrAdd(requested.FullName, _ => new(() => LoadDependency(requested))).Value;
	}

	private Assembly? LoadDependency(AssemblyName requested)
	{
		IEnumerable<string> candidates = _paths[requested.Name!].Concat([Path.Combine(Path.GetDirectoryName(_serializerPath)!, requested.Name + ".dll")]);
		foreach (string path in candidates.Where(path => File.Exists(path) && !_referencePaths.Contains(path)))
		{
			AssemblyName actual;
			try
			{
				actual = AssemblyName.GetAssemblyName(path);
			}
			catch (BadImageFormatException)
			{
				continue;
			}
			if (string.Equals(actual.Name, requested.Name, StringComparison.OrdinalIgnoreCase)
				&& string.Equals(actual.CultureName ?? "", requested.CultureName ?? "", StringComparison.OrdinalIgnoreCase)
				&& (requested.Version is null || actual.Version >= requested.Version)
				&& (requested.Version is null || (actual.GetPublicKeyToken() ?? []).SequenceEqual(requested.GetPublicKeyToken() ?? [])))
			{
				return LoadPrivate(path);
			}
		}
		throw new FileNotFoundException($"No compatible runtime assembly was found for isolated ConstExpr evaluation: '{requested.FullName}'.", requested.Name + ".dll");
	}

	public void Dispose()
	{
		_serializer = null;
		_serialize = null;
		_loaded.Clear();
		_dependencies.Clear();
		CollectibleEvaluationContext? context = _context;
		_context = null;
		context?.Dispose();
	}
}
#pragma warning restore RS1035
