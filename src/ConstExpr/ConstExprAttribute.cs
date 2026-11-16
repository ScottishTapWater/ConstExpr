namespace ConstExpr;

/// <summary>
/// Marks a method for build-time evaluation when its arguments can be evaluated independently of caller runtime state.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ConstExprAttribute : Attribute
{
}
