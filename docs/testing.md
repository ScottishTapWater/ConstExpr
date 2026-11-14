# Test suite

The suite uses TUnit and Microsoft.Testing.Platform. Production projects remain on .NET Standard 2.0. Both test projects run on .NET 10 with Roslyn 5.0.

| Group | Cases | Initial result |
| --- | ---: | --- |
| Core evaluation and serialisation | 2 | Fail at `NotImplementedException` stubs. |
| Compiler/execution controls | 3 | Pass. Original calls advance, a manual interceptor substitutes a result, and separate loads have fresh state. |
| Generator pipeline | 5 | Four fail for missing interception. The unmarked-source framework test passes. |
| Direct call forms | 8 | Fail because calls still execute the original method. |
| Caller constructs | 7 | Fail because calls still execute the original method. |
| Delegates | 4 | Lambda cases fail. Method-group characterisation cases pass with original execution. |
| Multiple call sites and unmarked calls | 4 | Three fail for missing interception. The unmarked runtime control passes. |
| Source edits and regeneration | 5 | Fail for missing interception or missing initial generated output. |

Initial total: 38 cases, 7 passed, 31 failed, none skipped. This is the pre-implementation baseline, not an expected-failure mode in the runner.

## Test boundaries

`ConstExpr.Core.Tests` separately checks evaluation of a parameterless counter method and integer serialisation round trips. Core exposes only stubs. The round-trip test does not depend on evaluation.

The basic Roslyn pipeline uses one source file, one class, and one direct parameterless call site inside a loop. Separate tests check generated interceptor presence, successful emission, repeated baked values, and an untouched runtime counter. Empty generator output cannot pass the generated-interceptor compilation test.

Direct-call cases cover unqualified, type-qualified, namespace-qualified, globally qualified, aliased, and `using static` calls, plus another class in the same file and another source file. Construct cases cover expression and statement lambdas, local and static local functions, a generic caller, an iterator, and an async caller after an await. Iterator results are enumerated, and async results are awaited.

Multiple-site cases require every site to be intercepted. They do not prescribe whether separate call sites share a build-time evaluation. Two separately marked counter methods each have their own state.

The Microsoft.CodeAnalysis.Testing framework verifies that unmarked source produces neither diagnostics nor generated files. Execution tests use Roslyn directly to run the real ConstExpr generator, emit the resulting compilation, and invoke it in a collectible assembly-load context. Each runtime fixture gets a fresh context. Only immutable reference metadata is shared across tests.

Regeneration tests retain the same generator driver and change its input compilation. They insert a line before a call, change text after it, move it to another file, add a site, or remove a site. They emit and execute the updated compilation with the actual interceptor location data. The removal case also requires an interceptor to have existed before the edit.

The manual interceptor is confined to the compiler control. Product tests never substitute a test generator for `ConstExprGenerator`.

## CI and release checks

CI retains package and formatting checks, runs all cases, and uploads TRX reports even when tests fail. CD runs the full suite before packaging or publishing. There are no skips, expected-exception assertions for the stubs, or `continue-on-error` settings.

Argument evaluation, marked generic methods, instance methods, object graphs, and custom serializers are outside this initial suite.
