using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

using PRoCon.Core;
using PRoCon.Core.Battlemap;
using PRoCon.Core.Maps;
using PRoCon.Core.Players;
using PRoCon.Core.Players.Items;
using PRoCon.Core.Plugin;
using PRoCon.Core.Plugin.Commands;

using CapturableEvent = PRoCon.Core.Events.CapturableEvents;
//Aliases
using EventType = PRoCon.Core.Events.EventType;

namespace PRoConEvents
{
    public partial class InsaneLimits
    {
        public String getClassName(Limit limit)
        {
            return "LimitEvaluator" + limit.id;
        }

        public String buildLimitSource(Limit limit)
        {
            String class_name = getClassName(limit);


            String class_source =
@"namespace PRoConEvents
{
    using System;
    using System.IO;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Collections.Generic;
    using System.Collections;
    using System.Net;
    using System.Net.Mail;
    using System.Data;
    using System.Threading;
    // .net 3.5 additions: procon 1.4.1.1 and later
    //using System.Linq;
    using System.Xml;



    class %class_name%
    {
        public Boolean FirstCheck(%first_check_arguments%)
        {
            try
            {
#pragma warning disable
            %FirstCheck%
#pragma warning restore
            }
            catch(Exception e)
            {
                plugin.DumpException(e, this.GetType().Name);
            }
            return false;
        }

        public Boolean SecondCheck(%second_check_arguments%)
        {
            try
            {
#pragma warning disable
            %SecondCheck%
#pragma warning restore
            }
            catch(Exception e)
            {
               plugin.DumpException(e, this.GetType().Name);
            }
            return true;
        }
    }
}";
            class_source = Regex.Replace(class_source, "%class_name%", class_name);

            // function arguments depend on event
            class_source = buildFunctionArguments(limit, "first_check_arguments", class_source);
            class_source = buildFunctionArguments(limit, "second_check_arguments", class_source);


            class_source = buildClassFunctionBody(limit, "FirstCheck", limit.FirstCheck, limit.FirstCheckCode, limit.FirstCheckExpression, class_source);
            class_source = buildClassFunctionBody(limit, "SecondCheck", limit.SecondCheck, limit.SecondCheckCode, limit.SecondCheckEpression, class_source);

            return class_source;
        }

        public String FormatFunctionCode(Limit limit, String method, String code)
        {
            if (method.Equals("FirstCheck"))
                code += "\nreturn false;";
            else if (method.Equals("SecondCheck"))
                code += "\nreturn true;";
            else
                throw new CompileException(FormatMessage("unknown method ^b" + method + "^n for " + limit.ShortDisplayName, MessageType.Error));


            List<String> lines = new List<String>(Regex.Split(code, "\n"));

            code = lines[0];
            String prefix = "            ";
            for (Int32 i = 1; i < lines.Count; i++)
                code += "\n" + prefix + lines[i];

            return code;
        }

        public String buildClassFunctionBody(Limit limit, String method, Limit.LimitType type, String code, String expression, String class_source)
        {
            String auto_return = String.Empty;


            // if disabled, or empty string make give it an auto-return value
            if (type.Equals(Limit.LimitType.Disabled) ||
                (type.Equals(Limit.LimitType.Code) && code.Length == 0) ||
                (type.Equals(Limit.LimitType.Expression) && expression.Length == 0))
                return Regex.Replace(class_source, "%" + method + "%", FormatFunctionCode(limit, method, ""));


            if (type.Equals(Limit.LimitType.Code))
                return Regex.Replace(class_source, "%" + method + "%", FormatFunctionCode(limit, method, code));

            else if (type.Equals(Limit.LimitType.Expression))
                return Regex.Replace(class_source, "%" + method + "%", "return ( (" + expression + ") == true);");
            else
                throw new CompileException(FormatMessage("unknown type for " + limit.ShortDisplayName, MessageType.Error));
        }


        public void SendCompilingMessage(Limit limit)
        {


            ConsoleWrite("Compiling " + limit.FullDisplayName + " - " + limit.Evaluation.ToString());

            String first_check = "^bfirst_check^n = ^i" + limit.FirstCheck.ToString() + "^n";

            if (limit.FirstCheckEmpty)
                ConsoleWarn("^bfirst_check_" + limit.FirstCheck.ToString().ToLower() + "^n is empty for " + limit.ShortDisplayName);


            if (limit.SecondCheckEmpty)
                ConsoleWarn("^bsecond_check_" + limit.SecondCheck.ToString().ToLower() + "^n is empty for " + limit.ShortDisplayName);

        }


        public void CompileLimit(Limit limit)
        {
            try
            {
                limit.Reset();

                if (compiler_references == null || compiler_options == null)
                {
                    var result = GenerateCompilerParameters();
                    compiler_references = result.references;
                    compiler_options = result.options;
                }

                limit.evaluator = null;
                limit.type = null;

                SendCompilingMessage(limit);

                if (limit.FirstCheckEmpty)
                    return;


                String class_source = buildLimitSource(limit);

                SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(class_source);

                CSharpCompilation compilation = CSharpCompilation.Create(
                    "LimitAssembly_" + limit.id,
                    syntaxTrees: new[] { syntaxTree },
                    references: compiler_references,
                    options: compiler_options);

                using (var ms = new MemoryStream())
                {
                    EmitResult emitResult = compilation.Emit(ms);

                    if (!emitResult.Success)
                    {
                        var errors = emitResult.Diagnostics
                            .Where(d => d.Severity == DiagnosticSeverity.Error)
                            .ToList();

                        // Display compilation errors.
                        ConsoleError("" + errors.Count + " error" + ((errors.Count > 1) ? "s" : "") + " compiling " + limit.FirstCheck.ToString());
                        foreach (Diagnostic diag in errors)
                        {
                            var lineSpan = diag.Location.GetLineSpan();
                            Int32 line = lineSpan.StartLinePosition.Line + 1;
                            Int32 column = lineSpan.StartLinePosition.Character + 1;
                            ConsoleError("(" + diag.Id + ", line: " + line + ", column: " + column + "):  " + diag.GetMessage());
                        }

                        return;
                    }
                    else
                    {
                        ms.Seek(0, SeekOrigin.Begin);
                        Assembly compiledAssembly = Assembly.Load(ms.ToArray());

                        String class_name = getClassName(limit);
                        Type class_type = compiledAssembly.GetType("PRoConEvents." + class_name);

                        ConstructorInfo class_ctor = class_type.GetConstructor(new Type[] { });
                        if (class_ctor == null)
                            throw new CompileException(FormatMessage("could not find constructor for ^b" + class_name + "^n", MessageType.Error));


                        Object class_object = class_ctor.Invoke(new Object[] { });


                        limit.evaluator = class_object;
                        limit.type = class_type;
                        return;
                    }
                }
            }
            catch (CompileException e)
            {
                LogWrite(e.Message);
            }
            catch (Exception e)
            {
                DumpException(e);
            }

            return;
        }
    }
}
