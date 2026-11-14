namespace ConstExpr.Roslyn.Tests;

public enum DirectCallForm
{
	Unqualified,
	TypeQualified,
	NamespaceQualified,
	GloballyQualified,
	TypeAlias,
	UsingStatic,
	OtherClassSameFile,
	OtherSourceFile
}
