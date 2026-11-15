namespace ConstExpr.Roslyn.Tests.Infrastructure;

internal static class FixtureSources
{
	internal const string Simple = """
		using ConstExpr;
		using ConstExpr.Roslyn.Tests.Infrastructure;

		namespace Fixture;

		public static class Counter
		{
			private static int counter;
			public static int Calls => counter;

			[ConstExpr]
			public static int CountUp() => ++counter;

			public static ExecutionResult Run()
			{
				int[] values = new int[3];
				for (int index = 0; index < values.Length; index++)
				{
					values[index] = CountUp();
				}
				return new ExecutionResult(values, Calls);
			}
		}
		""";

	internal const string Counter = """
		using ConstExpr;

		namespace Fixture;

		public static class Counter
		{
			private static int counter;
			public static int Calls => counter;

			[ConstExpr]
			public static int CountUp() => ++counter;
		}
		""";

	internal static string Caller(string expression = "Counter.CountUp()", string declarations = "", string imports = "") => $$"""
		using System;
		using System.Collections.Generic;
		using System.Threading.Tasks;
		using ConstExpr.Roslyn.Tests.Infrastructure;
		{{imports}}

		namespace Fixture;

		public static class Caller
		{
			public static ExecutionResult Run()
			{
				{{declarations}}
				int[] values = new int[3];
				for (int index = 0; index < values.Length; index++)
				{
					values[index] = {{expression}};
				}
				return new ExecutionResult(values, Counter.Calls);
			}
		}
		""";
}
