using System.Diagnostics;
using System.Reflection;

namespace ConstExpr.IntegrationTests;

[NotInParallel]
public sealed class PackagedConsumerTests : IDisposable
{
	private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("constexpr-package-");
	private readonly string _sourceDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

	[Test]
	public async Task ConsumerCanBuildAndRunUsingThePackage()
	{
		string configuration = typeof(PackagedConsumerTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
		string feed = await PackConstExpr(configuration);

		string project = Path.Combine(_directory.FullName, "Consumer.csproj");
		await File.WriteAllTextAsync(project, """
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
			    <TargetFramework>net10.0</TargetFramework>
			    <OutputType>Exe</OutputType>
			    <InterceptorsNamespaces>ConstExpr.Generated</InterceptorsNamespaces>
			    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
			  </PropertyGroup>
			  <ItemGroup>
			    <PackageReference Include="ConstExpr" Version="0.0.0-test" />
			  </ItemGroup>
			</Project>
			""");
		await File.WriteAllTextAsync(Path.Combine(_directory.FullName, "Program.cs"), """
			#nullable enable
			using ConstExpr;
			using System.Collections.Generic;

			public sealed class Node
			{
				public int Value { get; set; }
				public List<Node> Children { get; set; } = [];
			}

			public static class Program
			{
				[ConstExpr]
				public static Node GetValue()
				{
					Node node = new() { Value = 1 };
					node.Children.Add(node);
					return node;
				}

				public static void Main() => System.Console.WriteLine(GetValue().Value);
			}
			""");

		await RunDotnet("restore", project, "--source", feed, "--source", "https://api.nuget.org/v3/index.json",
			"--packages", Path.Combine(_directory.FullName, "packages"));
		await RunDotnet("build", project, "--configuration", configuration, "--no-restore", "-p:UseSharedCompilation=false");
		await RunDotnet("run", "--project", project, "--configuration", configuration, "--no-build", "--no-restore");
	}

	[Test]
	public async Task ConsumerEvaluatesMethodsUsingAProjectReferenceAndReconstructsItsDto()
	{
		string configuration = typeof(PackagedConsumerTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
		string feed = await PackConstExpr(configuration);
		await CreateHelperProject();

		string consumer = Directory.CreateDirectory(Path.Combine(_directory.FullName, "Consumer")).FullName;
		string project = Path.Combine(consumer, "Consumer.csproj");
		await File.WriteAllTextAsync(project, """
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
			    <TargetFramework>net10.0</TargetFramework>
			    <OutputType>Exe</OutputType>
			    <InterceptorsNamespaces>ConstExpr.Generated</InterceptorsNamespaces>
			    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
			  </PropertyGroup>
			  <ItemGroup>
			    <PackageReference Include="ConstExpr" Version="0.0.0-test" />
			    <ProjectReference Include="../Helper/Helper.csproj" />
			  </ItemGroup>
			</Project>
			""");
		await File.WriteAllTextAsync(Path.Combine(consumer, "Program.cs"), """
			using ConstExpr;
			using HelperLibrary;

			public static class Program
			{
				private static int calls;

				[ConstExpr]
				public static Calculator.Measurement GetValue()
				{
					calls++;
					return Calculator.Create(21);
				}

				public static int Main() => GetValue().Value == 42 && calls == 0 ? 0 : 1;
			}
			""");

		await RunDotnet("restore", project, "--source", feed, "--source", "https://api.nuget.org/v3/index.json",
			"--packages", Path.Combine(_directory.FullName, "packages"));
		await RunDotnet("build", project, "--configuration", configuration, "--no-restore", "-p:UseSharedCompilation=false");
		await RunDotnet("run", "--project", project, "--configuration", configuration, "--no-build", "--no-restore");
	}

	[Test]
	public async Task ConsumerEvaluatesANuGetDependencyUsingItsRuntimeAssetAndTransitiveDependency()
	{
		string configuration = typeof(PackagedConsumerTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
		string feed = await PackConstExpr(configuration);
		string helper = await CreateHelperProject();
		await RunDotnet("pack", Path.Combine(_directory.FullName, "Support", "Support.csproj"),
			"--configuration", configuration, "--output", feed);
		await RunDotnet("pack", helper, "--configuration", configuration, "--output", feed);

		string consumer = Directory.CreateDirectory(Path.Combine(_directory.FullName, "Consumer")).FullName;
		string project = Path.Combine(consumer, "Consumer.csproj");
		await File.WriteAllTextAsync(project, """
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
			    <TargetFramework>net10.0</TargetFramework>
			    <OutputType>Exe</OutputType>
			    <InterceptorsNamespaces>ConstExpr.Generated</InterceptorsNamespaces>
			    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
			  </PropertyGroup>
			  <ItemGroup>
			    <PackageReference Include="ConstExpr" Version="0.0.0-test" />
			    <PackageReference Include="ConstExpr.TestHelper" Version="0.0.0-test" />
			  </ItemGroup>
			</Project>
			""");
		await File.WriteAllTextAsync(Path.Combine(consumer, "Program.cs"), """
			using ConstExpr;
			using HelperLibrary;

			public static class Program
			{
				private static int calls;

				[ConstExpr]
				public static Calculator.Measurement GetValue()
				{
					calls++;
					return Calculator.Create(21);
				}

				public static int Main() => GetValue().Value == 42 && calls == 0 ? 0 : 1;
			}
			""");

		await RunDotnet("restore", project, "--source", feed, "--source", "https://api.nuget.org/v3/index.json",
			"--packages", Path.Combine(_directory.FullName, "packages"));
		await RunDotnet("build", project, "--configuration", configuration, "--no-restore", "-p:UseSharedCompilation=false");
		await RunDotnet("run", "--project", project, "--configuration", configuration, "--no-build", "--no-restore");
	}

	private async Task<string> PackConstExpr(string configuration)
	{
		string feed = Path.Combine(_directory.FullName, "feed");
		await RunDotnet("pack", Path.Combine(_sourceDirectory, "ConstExpr", "ConstExpr.csproj"),
			"--configuration", configuration, "--no-build", "--no-restore", "--output", feed, "-p:PackageVersion=0.0.0-test");
		return feed;
	}

	private async Task<string> CreateHelperProject()
	{
		string support = Directory.CreateDirectory(Path.Combine(_directory.FullName, "Support")).FullName;
		await File.WriteAllTextAsync(Path.Combine(support, "Support.csproj"), """
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
			    <TargetFramework>net10.0</TargetFramework>
			    <PackageId>ConstExpr.TestSupport</PackageId>
			    <Version>0.0.0-test</Version>
			  </PropertyGroup>
			</Project>
			""");
		await File.WriteAllTextAsync(Path.Combine(support, "Calculation.cs"), """
			namespace SupportLibrary;

			public static class Calculation
			{
				public static int Double(int value) => value * 2;
			}
			""");

		string helper = Directory.CreateDirectory(Path.Combine(_directory.FullName, "Helper")).FullName;
		string project = Path.Combine(helper, "Helper.csproj");
		await File.WriteAllTextAsync(project, """
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
			    <TargetFramework>net10.0</TargetFramework>
			    <PackageId>ConstExpr.TestHelper</PackageId>
			    <Version>0.0.0-test</Version>
			  </PropertyGroup>
			  <ItemGroup>
			    <ProjectReference Include="../Support/Support.csproj" />
			    <None Include="$(IntermediateOutputPath)ref/Helper.dll" Pack="true" PackagePath="ref/net10.0/Helper.dll" />
			  </ItemGroup>
			</Project>
			""");
		await File.WriteAllTextAsync(Path.Combine(helper, "Calculator.cs"), """
			namespace HelperLibrary;

			public static class Calculator
			{
				public sealed class Measurement
				{
					public int Value { get; set; }
				}

				public static Measurement Create(int input) => new() { Value = SupportLibrary.Calculation.Double(input) };
			}
			""");
		return project;
	}

	private async Task RunDotnet(params string[] arguments)
	{
		ProcessStartInfo start = new("dotnet")
		{
			WorkingDirectory = _sourceDirectory,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
		};
		foreach (string argument in arguments)
		{
			start.ArgumentList.Add(argument);
		}
		using Process process = Process.Start(start)!;
		Task<string> output = process.StandardOutput.ReadToEndAsync();
		Task<string> error = process.StandardError.ReadToEndAsync();
		await process.WaitForExitAsync();
		string[] logs = await Task.WhenAll(output, error);
		if (process.ExitCode != 0)
		{
			throw new InvalidOperationException($"dotnet {string.Join(" ", arguments)} failed.\n{string.Join("\n", logs)}");
		}
	}

	public void Dispose() => _directory.Delete(recursive: true);
}
