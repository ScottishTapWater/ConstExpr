using ConstExpr.Roslyn.Tests.Infrastructure;

namespace ConstExpr.Roslyn.Tests;

public sealed class ObjectGraphTests
{
	[Test]
	[MethodDataSource(nameof(Graphs))]
	public async Task ReconstructedGraphsPreserveValuesAndReferenceIdentity(GraphTestCase testCase)
	{
		string source = $$"""
			#nullable enable
			using ConstExpr;
			using System.Collections.Generic;

			public sealed class Node
			{
				public int Value { get; set; }
				public Node? Left { get; set; }
				public Node? Right { get; set; }
				public Node? Next { get; set; }
				public List<Node> Children { get; set; } = [];
				public List<Node> Others { get; set; } = [];
			}

			public static class Factory
			{
				private static int counter;

				[ConstExpr]
				public static {{testCase.Type}} Create(int key)
				{
					counter++;
					{{testCase.Body}}
				}

				public static bool Run()
				{
					{{testCase.Type}} result = Create(3);
					return ({{testCase.Condition}}) && counter == 0;
				}
			}
			""";

		bool result = await ConstExprFixture.CompileAndRun<bool>([new("Program.cs", source)], "Factory.Run");

		await Assert.That(result).IsTrue();
	}

	[Test]
	public async Task MutationsThroughASharedChildAreVisibleThroughAllAliases()
	{
		const string source = """
			#nullable enable
			using ConstExpr;

			public sealed class Node
			{
				public int Value { get; set; }
				public Node? Left { get; set; }
				public Node? Right { get; set; }
			}

			public static class Factory
			{
				[ConstExpr]
				public static Node Create()
				{
					Node child = new();
					return new() { Left = child, Right = child };
				}

				public static bool Run()
				{
					Create().Left!.Value = 42;
					return Create().Right!.Value == 42;
				}
			}
			""";

		bool result = await ConstExprFixture.CompileAndRun<bool>([new("Program.cs", source)], "Factory.Run");

		await Assert.That(result).IsTrue();
	}

	[Test]
	public async Task DifferentArgumentCombinationsHaveIndependentCyclicGraphs()
	{
		const string source = """
			#nullable enable
			using ConstExpr;

			public sealed class Node
			{
				public int Value { get; set; } = 1;
				public Node? Next { get; set; }
			}

			public static class Factory
			{
				[ConstExpr]
				public static Node Create(int key)
				{
					Node node = new();
					node.Next = node;
					return node;
				}

				public static bool Run()
				{
					Node first = Create(1);
					Node second = Create(2);
					first.Value = 42;
					return object.ReferenceEquals(first, first.Next) && object.ReferenceEquals(second, second.Next)
						&& object.ReferenceEquals(first, Create(1)) && second.Value == 1;
				}
			}
			""";

		bool result = await ConstExprFixture.CompileAndRun<bool>([new("Program.cs", source)], "Factory.Run");

		await Assert.That(result).IsTrue();
	}

	public static IEnumerable<Func<GraphTestCase>> Graphs()
	{
		yield return () => new("Nested objects", "Node", "return new() { Value = key, Left = new() { Value = key + 1 } };",
			"result.Value == 3 && result.Left!.Value == 4");
		yield return () => new("Shared child", "Node", "Node child = new() { Value = key }; return new() { Left = child, Right = child };",
			"object.ReferenceEquals(result.Left, result.Right) && result.Left!.Value == 3");
		yield return () => new("Self cycle", "Node", "Node node = new() { Value = key }; node.Next = node; return node;",
			"object.ReferenceEquals(result, result.Next) && result.Value == 3");
		yield return () => new("Mutual cycle", "Node", "Node first = new(); Node second = new(); first.Next = second; second.Next = first; return first;",
			"object.ReferenceEquals(result, result.Next!.Next) && !object.ReferenceEquals(result, result.Next)");
		yield return () => new("Shared list entries", "List<Node>", "Node child = new() { Value = key }; return [child, child];",
			"result.Count == 2 && object.ReferenceEquals(result[0], result[1]) && result[0].Value == 3");
		yield return () => new("Cycle through collection", "Node", "Node node = new(); node.Children.Add(node); return node;",
			"object.ReferenceEquals(result, result.Children[0])");
		yield return () => new("Self-referential collection", "List<object>", "List<object> values = []; values.Add(values); return values;",
			"object.ReferenceEquals(result, result[0])");
		yield return () => new("Shared collection", "Node", "List<Node> values = [new() { Value = key }]; return new() { Children = values, Others = values };",
			"object.ReferenceEquals(result.Children, result.Others) && result.Children[0].Value == 3");
	}

	public sealed record GraphTestCase(string Name, string Type, string Body, string Condition)
	{
		public override string ToString() => Name;
	}
}
