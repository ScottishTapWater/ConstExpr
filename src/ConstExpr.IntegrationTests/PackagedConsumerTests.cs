using System.Diagnostics;
using System.Reflection;

namespace ConstExpr.IntegrationTests;

public sealed class PackagedConsumerTests : IDisposable
{
	private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("constexpr-package-");
	private readonly string _sourceDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

	[Test]
	public async Task ConsumerCanBuildAndRunUsingThePackage()
	{
		string feed = Path.Combine(_directory.FullName, "feed");
		string configuration = typeof(PackagedConsumerTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
		await RunDotnet("pack", Path.Combine(_sourceDirectory, "ConstExpr", "ConstExpr.csproj"),
			"--configuration", configuration, "--no-build", "--no-restore", "--output", feed, "-p:PackageVersion=0.0.0-test");

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
			using ConstExpr;

			public static class Program
			{
				[ConstExpr]
				public static int[] GetValue() => [1, 2, 3];

				public static void Main() => System.Console.WriteLine(GetValue()[0]);
			}
			""");

		await RunDotnet("restore", project, "--source", feed, "--source", "https://api.nuget.org/v3/index.json",
			"--packages", Path.Combine(_directory.FullName, "packages"));
		await RunDotnet("build", project, "--configuration", configuration, "--no-restore", "-p:UseSharedCompilation=false");
		await RunDotnet("run", "--project", project, "--configuration", configuration, "--no-build", "--no-restore");
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
