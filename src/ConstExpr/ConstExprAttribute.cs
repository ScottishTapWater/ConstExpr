namespace ConstExpr;

/// <summary>
/// Marks a method for build-time evaluation when its arguments are compile-time constants.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ConstExprAttribute : Attribute
{
}
