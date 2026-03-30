using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

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
    using System.Text.RegularExpressions;
    using System.Collections.Generic;
    // .net 3.5 additions: procon 1.4.1.1 and later
    //using System.Linq;

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
                limit.evaluator = null;
                limit.type = null;

                SendCompilingMessage(limit);

                // Dynamic limit compilation is not yet supported in Procon v2.
                // Roslyn (Microsoft.CodeAnalysis) assemblies are not available to plugins at runtime.
                ConsoleError("Dynamic limit compilation is not yet supported in Procon v2. Limit " + limit.ShortDisplayName + " will not be compiled.");
                return;
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
