using System.IO;
using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Operations;

namespace ConstExpr.Roslyn.Evaluation;

internal sealed class CallExpressionCompiler
{
	private readonly EvaluationAssemblyLoader _loader;

	internal CallExpressionCompiler(EvaluationAssemblyLoader loader)
	{
		_loader = loader;
	}

	internal MethodInfo Compile(InvocationExpressionSyntax call, SemanticModel model, ITypeSymbol returnType, CancellationToken cancellationToken)
	{
		string name = "__ConstExprEvaluate" + Guid.NewGuid().ToString("N");
		ExpressionSyntax expression = (ExpressionSyntax)new CallExpressionRewriter(model).Visit(call)!;
		expression = PreserveOverflowContext(expression, call, ((CSharpCompilationOptions)model.Compilation.Options).CheckOverflow);
		MethodDeclarationSyntax entrypoint = SyntaxFactory.MethodDeclaration(SyntaxFactory.ParseTypeName(returnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)), name)
			.WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PrivateKeyword), SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
			.WithExpressionBody(SyntaxFactory.ArrowExpressionClause(expression))
			.WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
		if (call.Ancestors().Any(ancestor => ancestor.ChildTokens().Any(token => token.IsKind(SyntaxKind.UnsafeKeyword))))
		{
			entrypoint = entrypoint.AddModifiers(SyntaxFactory.Token(SyntaxKind.UnsafeKeyword));
		}
		List<MemberDeclarationSyntax> entrypoints = [entrypoint];
		if (call.DescendantNodes().OfType<AwaitExpressionSyntax>().Any(awaitExpression => SymbolEqualityComparer.Default.Equals(
			model.GetEnclosingSymbol(awaitExpression.SpanStart, cancellationToken), model.GetEnclosingSymbol(call.SpanStart, cancellationToken))))
		{
			string asyncName = name + "Awaited";
			MethodDeclarationSyntax asyncEntrypoint = entrypoint.WithIdentifier(SyntaxFactory.Identifier(asyncName))
				.WithReturnType(SyntaxFactory.ParseTypeName($"global::System.Threading.Tasks.Task<{returnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}>"))
				.AddModifiers(SyntaxFactory.Token(SyntaxKind.AsyncKeyword));
			entrypoints[0] = entrypoint.WithExpressionBody(SyntaxFactory.ArrowExpressionClause(SyntaxFactory.ParseExpression($"{asyncName}().GetAwaiter().GetResult()")));
			entrypoints.Add(asyncEntrypoint);
		}

		SyntaxNode root = call.SyntaxTree.GetRoot(cancellationToken);
		TypeDeclarationSyntax? containingType = call.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
		SyntaxNode updated = containingType is not null
			? root.ReplaceNode(containingType, containingType.AddMembers(entrypoints.ToArray()))
			: ((CompilationUnitSyntax)root).AddMembers(SyntaxFactory.ClassDeclaration(name)
				.WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
				.AddMembers(entrypoints.ToArray()));
		SyntaxTree tree = call.SyntaxTree.WithRootAndOptions(updated, call.SyntaxTree.Options);
		CSharpCompilation compilation = ((CSharpCompilation)model.Compilation).ReplaceSyntaxTree(call.SyntaxTree, tree)
			.WithAssemblyName(model.Compilation.AssemblyName + "." + name);
		using MemoryStream stream = new();
		EmitResult result = compilation.Emit(stream, cancellationToken: cancellationToken);
		if (!result.Success)
		{
			throw new InvalidOperationException(string.Join("\n", result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));
		}
		Assembly assembly = _loader.Load(stream.ToArray());
		return assembly.GetTypes().SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
			.Single(method => method.Name == name);
	}

	private static ExpressionSyntax PreserveOverflowContext(ExpressionSyntax expression, SyntaxNode original, bool defaultChecked)
	{
		SyntaxNode? context = original.Ancestors().FirstOrDefault(node => node is CheckedExpressionSyntax or CheckedStatementSyntax);
		bool isChecked = context is null ? defaultChecked : context.IsKind(SyntaxKind.CheckedExpression) || context.IsKind(SyntaxKind.CheckedStatement);
		return SyntaxFactory.CheckedExpression(isChecked ? SyntaxKind.CheckedExpression : SyntaxKind.UncheckedExpression, expression);
	}

	private sealed class CallExpressionRewriter : CSharpSyntaxRewriter
	{
		private readonly SemanticModel _model;

		internal CallExpressionRewriter(SemanticModel model)
		{
			_model = model;
		}

		public override SyntaxNode? Visit(SyntaxNode? node)
		{
			if (node is IdentifierNameSyntax identifier && _model.GetSymbolInfo(identifier).Symbol is ILocalSymbol { IsConst: true } local)
			{
				VariableDeclaratorSyntax declaration = (VariableDeclaratorSyntax)local.DeclaringSyntaxReferences.Single().GetSyntax();
				ExpressionSyntax initializer = (ExpressionSyntax)Visit(declaration.Initializer!.Value)!;
				ExpressionSyntax cast = SyntaxFactory.CastExpression(SyntaxFactory.ParseTypeName(local.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)),
					SyntaxFactory.ParenthesizedExpression(initializer));
				return PreserveOverflowContext(cast, declaration, ((CSharpCompilationOptions)_model.Compilation.Options).CheckOverflow);
			}
			if (node is InvocationExpressionSyntax && _model.GetOperation(node) is INameOfOperation nameOf)
			{
				return ConstantExpression(nameOf.ConstantValue.Value, nameOf.Type!);
			}
			return base.Visit(node);
		}

		public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
		{
			InvocationExpressionSyntax rewritten = (InvocationExpressionSyntax)base.VisitInvocationExpression(node)!;
			return _model.GetOperation(node) is IInvocationOperation invocation
					? rewritten.WithArgumentList(rewritten.ArgumentList.WithArguments(PreserveCallerInformation(rewritten.ArgumentList.Arguments, invocation.Arguments))) : rewritten;
		}

		public override SyntaxNode? VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
		{
			ObjectCreationExpressionSyntax rewritten = (ObjectCreationExpressionSyntax)base.VisitObjectCreationExpression(node)!;
			ArgumentListSyntax arguments = rewritten.ArgumentList ?? SyntaxFactory.ArgumentList();
			return _model.GetOperation(node) is IObjectCreationOperation creation
				? rewritten.WithArgumentList(arguments.WithArguments(PreserveCallerInformation(arguments.Arguments, creation.Arguments))) : rewritten;
		}

		public override SyntaxNode? VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node)
		{
			ImplicitObjectCreationExpressionSyntax rewritten = (ImplicitObjectCreationExpressionSyntax)base.VisitImplicitObjectCreationExpression(node)!;
			return _model.GetOperation(node) is IObjectCreationOperation creation
				? rewritten.WithArgumentList(rewritten.ArgumentList.WithArguments(PreserveCallerInformation(rewritten.ArgumentList.Arguments, creation.Arguments))) : rewritten;
		}

		public override SyntaxNode? VisitElementAccessExpression(ElementAccessExpressionSyntax node)
		{
			ElementAccessExpressionSyntax rewritten = (ElementAccessExpressionSyntax)base.VisitElementAccessExpression(node)!;
			return _model.GetOperation(node) is IPropertyReferenceOperation property
				? rewritten.WithArgumentList(rewritten.ArgumentList.WithArguments(PreserveCallerInformation(rewritten.ArgumentList.Arguments, property.Arguments))) : rewritten;
		}

		public override SyntaxNode? VisitInitializerExpression(InitializerExpressionSyntax node)
		{
			InitializerExpressionSyntax rewritten = (InitializerExpressionSyntax)base.VisitInitializerExpression(node)!;
			if (!node.IsKind(SyntaxKind.CollectionInitializerExpression)
				|| _model.GetOperation(node) is not IObjectOrCollectionInitializerOperation initializer)
			{
				return rewritten;
			}
			for (int index = 0; index < initializer.Initializers.Length; index++)
			{
				if (initializer.Initializers[index] is not IInvocationOperation invocation
					|| !invocation.Arguments.Any(argument => argument.IsImplicit && HasCallerInformation(argument.Parameter!)))
				{
					continue;
				}
				ExpressionSyntax element = rewritten.Expressions[index];
				SeparatedSyntaxList<ExpressionSyntax> arguments = element is InitializerExpressionSyntax complex
					? complex.Expressions : SyntaxFactory.SingletonSeparatedList(element);
				foreach (IArgumentOperation argument in invocation.Arguments.Where(argument => argument.IsImplicit && argument.ArgumentKind == ArgumentKind.DefaultValue))
				{
					ExpressionSyntax value = argument.Value.ConstantValue is { HasValue: true } constant
						? ConstantExpression(constant.Value, argument.Parameter!.Type)
						: SyntaxFactory.DefaultExpression(SyntaxFactory.ParseTypeName(argument.Parameter!.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
					arguments = arguments.Add(value);
				}
				rewritten = rewritten.WithExpressions(rewritten.Expressions.Replace(element,
					SyntaxFactory.InitializerExpression(SyntaxKind.ComplexElementInitializerExpression, arguments)));
			}
			return rewritten;
		}

		private static SeparatedSyntaxList<ArgumentSyntax> PreserveCallerInformation(SeparatedSyntaxList<ArgumentSyntax> arguments, ImmutableArray<IArgumentOperation> boundArguments)
		{
			foreach (IArgumentOperation argument in boundArguments.Where(argument => argument.IsImplicit
				&& argument.ArgumentKind == ArgumentKind.DefaultValue && HasCallerInformation(argument.Parameter!)))
			{
				arguments = arguments.Add(SyntaxFactory.Argument(ConstantExpression(argument.Value.ConstantValue.Value, argument.Parameter!.Type))
					.WithNameColon(SyntaxFactory.NameColon((IdentifierNameSyntax)SyntaxFactory.ParseName("@" + argument.Parameter.Name))));
			}
			return arguments;
		}

		private static bool HasCallerInformation(IParameterSymbol parameter) => parameter.GetAttributes().Any(attribute =>
			attribute.AttributeClass?.ContainingNamespace.ToDisplayString() == "System.Runtime.CompilerServices"
			&& attribute.AttributeClass.Name is "CallerMemberNameAttribute" or "CallerFilePathAttribute" or "CallerLineNumberAttribute" or "CallerArgumentExpressionAttribute");

		private static ExpressionSyntax ConstantExpression(object? value, ITypeSymbol type)
		{
			ExpressionSyntax literal = value switch
			{
				null => SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression),
				bool boolean => SyntaxFactory.LiteralExpression(boolean ? SyntaxKind.TrueLiteralExpression : SyntaxKind.FalseLiteralExpression),
				string text => SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(text)),
				char character => SyntaxFactory.LiteralExpression(SyntaxKind.CharacterLiteralExpression, SyntaxFactory.Literal(character)),
				_ => SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, value switch
				{
					sbyte or byte or short or ushort or int => SyntaxFactory.Literal(Convert.ToInt32(value)),
					uint number => SyntaxFactory.Literal(number),
					long number => SyntaxFactory.Literal(number),
					ulong number => SyntaxFactory.Literal(number),
					float number => SyntaxFactory.Literal(number),
					double number => SyntaxFactory.Literal(number),
					decimal number => SyntaxFactory.Literal(number),
					_ => throw new InvalidOperationException($"Unsupported constant type '{type}'.")
				})
			};
			return SyntaxFactory.CastExpression(SyntaxFactory.ParseTypeName(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)),
				SyntaxFactory.ParenthesizedExpression(literal));
		}
	}
}
