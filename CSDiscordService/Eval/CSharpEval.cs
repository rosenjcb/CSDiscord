using CSDiscordService.Eval.ResultModels;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Scripting;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;

namespace CSDiscordService.Eval
{
    public class CSharpEval
    {
        private static readonly ImmutableArray<string> DefaultImports =
            ImmutableArray.Create(
                "Newtonsoft.Json",
                "Newtonsoft.Json.Linq",
                "System",
                "System.Collections",
                "System.Collections.Concurrent",
                "System.Collections.Immutable",
                "System.Collections.Generic",
                "System.Dynamic",
                "System.Security.Cryptography",
                "System.Globalization",
                "System.IO",
                "System.Linq",
                "System.Linq.Expressions",
                "System.Net",
                "System.Net.Http",
                "System.Numerics",
                "System.Reflection",
                "System.Reflection.Emit",
                "System.Runtime.CompilerServices",
                "System.Runtime.InteropServices",
                "System.Runtime.Intrinsics",
                "System.Runtime.Intrinsics.X86",
                "System.Text",
                "System.Text.RegularExpressions",
                "System.Threading",
                "System.Threading.Tasks",
                "System.Text.Json",
                "CSDiscordService.Eval"
            );

        private static readonly ImmutableArray<Assembly> DefaultReferences =
            ImmutableArray.Create(
                typeof(Enumerable).GetTypeInfo().Assembly,
                typeof(HttpClient).GetTypeInfo().Assembly,
                typeof(List<>).GetTypeInfo().Assembly,
                typeof(string).GetTypeInfo().Assembly,
                typeof(Unsafe).GetTypeInfo().Assembly,
                typeof(ValueTuple).GetTypeInfo().Assembly,
                typeof(Globals).GetTypeInfo().Assembly,
                typeof(Memory<>).GetTypeInfo().Assembly
            );

        private static readonly ScriptOptions Options =
            ScriptOptions.Default
                .WithLanguageVersion(LanguageVersion.Preview)
                .WithImports(DefaultImports)
                .WithReferences(DefaultReferences);

        private static readonly ImmutableArray<DiagnosticAnalyzer> Analyzers =
            ImmutableArray.Create<DiagnosticAnalyzer>(new BlacklistedTypesAnalyzer());

        private static readonly Random random = new Random();
        private readonly JsonSerializerOptions _serializerOptions;

        public CSharpEval(JsonSerializerOptions serializerOptons)
        {
            _serializerOptions = serializerOptons;
        }

        public async Task<EvalResult> RunEvalAsync(string code)
        {
            var sb = new StringBuilder();
            using var textWr = new ConsoleLikeStringWriter(sb);
            var env = new BasicEnvironment();

            var sw = Stopwatch.StartNew();
            var eval = CSharpScript.Create(code, Options, typeof(Globals));

            var compilation = eval.GetCompilation().WithAnalyzers(Analyzers);

            var compileResult = await compilation.GetAllDiagnosticsAsync();
            var compileErrors = compileResult.Where(a => a.Severity == DiagnosticSeverity.Error).ToImmutableArray();
            sw.Stop();

            var compileTime = sw.Elapsed;
            if (compileErrors.Length > 0)
            {
                return EvalResult.CreateErrorResult(code, sb.ToString(), sw.Elapsed, compileErrors);
            }

            var globals = new Globals();
            Globals.Random = random;
            Globals.Console = textWr;
            Globals.Environment = env;

            sw.Restart();
            ScriptState<object> result;

            try
            {
                result = await eval.RunAsync(globals, ex => true);
            }
            catch (CompilationErrorException ex)
            {
                return EvalResult.CreateErrorResult(code, sb.ToString(), sw.Elapsed, ex.Diagnostics);
            }
            sw.Stop();

            var evalResult = new EvalResult(result, sb.ToString(), sw.Elapsed, compileTime);
            //this hack is to test if we're about to send an object that can't be serialized back to the caller.
            //if the object can't be serialized, return a failure instead.
            try
            {
                _ = JsonSerializer.Serialize(evalResult, _serializerOptions);
            }
            catch (Exception ex)
            {
                evalResult = new EvalResult
                {
                    Code = code,
                    CompileTime = compileTime,
                    ConsoleOut = sb.ToString(),
                    ExecutionTime = sw.Elapsed,
                    Exception = $"An exception occurred when serializing the response: {ex.GetType().Name}: {ex.Message}",
                    ExceptionType = ex.GetType().Name
                };
            }

            return evalResult;
        }
    }
}
