namespace ConstExpr;

/// <summary>
/// Marks a method whose result is evaluated during the build.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ConstExprAttribute : Attribute
{
}
