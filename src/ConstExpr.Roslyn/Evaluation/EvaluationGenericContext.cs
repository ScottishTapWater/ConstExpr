using System.Reflection;

namespace ConstExpr.Roslyn.Evaluation;

internal static class EvaluationGenericContext
{
	internal static MethodInfo Close(MethodInfo method, string marker, CancellationToken cancellationToken)
	{
		if (!method.IsGenericMethodDefinition)
		{
			return method;
		}

		// The input analysis has already excluded values and types dependent on these parameters.
		// Roslyn still copies the caller's parameters and constraints onto the local function.
		Type[] parameters = method.GetGenericArguments();
		IEnumerable<CustomAttributeTypedArgument> contextTypes = (IEnumerable<CustomAttributeTypedArgument>)method.GetCustomAttributesData()
			.Single(attribute => attribute.AttributeType.FullName == marker).ConstructorArguments[1].Value!;
		Type[] candidates = contextTypes.Select(argument => (Type)argument.Value!).Concat(new[] { typeof(object), typeof(int), typeof(string) })
			.Concat(parameters.SelectMany(parameter => parameter.GetGenericParameterConstraints()))
			.Concat(method.Module.Assembly.GetTypes())
			.Where(type => !type.ContainsGenericParameters && !type.IsPointer && !type.IsByRef && type != typeof(void))
			.Distinct().ToArray();
		Type[] arguments = new Type[parameters.Length];
		return FindContext(0) ?? throw new InvalidOperationException("No closed evaluation context satisfies the caller's generic constraints.");

		MethodInfo? FindContext(int index)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (index == arguments.Length)
			{
				try
				{
					return method.MakeGenericMethod(arguments);
				}
				catch (ArgumentException)
				{
					return null;
				}
			}
			foreach (Type candidate in candidates.Where(type => SatisfiesClosedConstraints(parameters[index], type)))
			{
				arguments[index] = candidate;
				if (FindContext(index + 1) is { } closed)
				{
					return closed;
				}
			}
			return null;
		}
	}

	private static bool SatisfiesClosedConstraints(Type parameter, Type candidate)
	{
		GenericParameterAttributes attributes = parameter.GenericParameterAttributes;
		return (!attributes.HasFlag(GenericParameterAttributes.ReferenceTypeConstraint) || !candidate.IsValueType)
			&& (!attributes.HasFlag(GenericParameterAttributes.NotNullableValueTypeConstraint)
				|| candidate.IsValueType && Nullable.GetUnderlyingType(candidate) is null)
			&& (!attributes.HasFlag(GenericParameterAttributes.DefaultConstructorConstraint)
				|| candidate.IsValueType || !candidate.IsAbstract && candidate.GetConstructor(Type.EmptyTypes) is not null)
			&& parameter.GetGenericParameterConstraints().Where(type => !type.ContainsGenericParameters).All(type => type.IsAssignableFrom(candidate));
	}
}
