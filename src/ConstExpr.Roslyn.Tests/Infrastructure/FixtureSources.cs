namespace ConstExpr.Roslyn.Tests.Infrastructure;

internal static class FixtureSources
{
	internal const string Simple = """
		using ConstExpr;

		namespace Fixture;

		public static class Counter
		{
			private static int counter;
			public static int Calls => counter;

			[ConstExpr]
			public static int CountUp() => ++counter;

			public static int[] Run()
			{
				int[] values = new int[3];
				for (int index = 0; index < values.Length; index++)
				{
					values[index] = CountUp();
				}
				return values;
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
		{{imports}}

		namespace Fixture;

		public static class Caller
		{
			public static int[] Run()
			{
				{{declarations}}
				int[] values = new int[3];
				for (int index = 0; index < values.Length; index++)
				{
					values[index] = {{expression}};
				}
				return values;
			}
		}
		""";
}
