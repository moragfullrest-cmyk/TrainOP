using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Text;
using TrainOP.Generators.Models;

namespace TrainOP.Generators
{
    /// <summary>
    /// Emits Roslyn interceptors that pass compile-time chain keys into generated station adapters.
    /// </summary>
    internal static class TrainRouteStationInterceptorsEmitter
    {
        /// <summary>
        /// Emits interceptor methods for chain-bound legacy station call sites.
        /// </summary>
        public static void Emit(
            SourceProductionContext context,
            Compilation compilation,
            IReadOnlyList<InterceptorSite> sites)
        {
            if (sites == null || sites.Count == 0)
            {
                return;
            }

            var source = new StringBuilder();
            source.AppendLine("using System;");
            source.AppendLine("using System.Threading;");
            source.AppendLine("using System.Threading.Tasks;");
            source.AppendLine();
            AppendInterceptsLocationAttributeDefinition(source);
            source.AppendLine("namespace TrainOP.Generated");
            source.AppendLine("{");
            source.AppendLine("    internal static class TrainRouteStationInterceptors");
            source.AppendLine("    {");

            var emittedCount = 0;
            for (var i = 0; i < sites.Count; i++)
            {
                if (EmitSite(source, compilation, emittedCount, sites[i]))
                {
                    emittedCount++;
                }
            }

            source.AppendLine("    }");
            source.AppendLine("}");

            if (emittedCount == 0)
            {
                return;
            }

            context.AddSource(
                "TrainRouteStation.Interceptors.g.cs",
                SourceText.From(source.ToString(), Encoding.UTF8));
        }

        private static void AppendInterceptsLocationAttributeDefinition(StringBuilder source)
        {
            source.AppendLine("namespace System.Runtime.CompilerServices");
            source.AppendLine("{");
            source.AppendLine("    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]");
            source.AppendLine("    file sealed class InterceptsLocationAttribute : Attribute");
            source.AppendLine("    {");
            source.AppendLine("        public InterceptsLocationAttribute(int version, string data)");
            source.AppendLine("        {");
            source.AppendLine("        }");
            source.AppendLine("    }");
            source.AppendLine("}");
            source.AppendLine();
        }

        private static bool EmitSite(
            StringBuilder source,
            Compilation compilation,
            int siteIndex,
            InterceptorSite site)
        {
            var attributeBuilder = new StringBuilder();
            if (!InterceptorLocationFormatter.TryAppendAttribute(
                attributeBuilder,
                compilation,
                site.Invocation,
                out var displayLocation))
            {
                return false;
            }

            var attribute = attributeBuilder.ToString().TrimEnd();
            source.AppendLine();
            source.Append("        ").Append(attribute);
            if (!string.IsNullOrEmpty(displayLocation))
            {
                source.Append(" // ").Append(displayLocation);
            }

            source.AppendLine();
            source.Append("        public static TrainRoute ")
                .Append(site.RouteMethodName)
                .Append('_')
                .Append(siteIndex)
                .Append("(this TrainRoute route, string stationName, ")
                .Append(site.HandlerTypeName)
                .AppendLine(" handler)");
            source.AppendLine("        {");
            source.AppendLine("            if (route == null) throw new ArgumentNullException(nameof(route));");
            source.AppendLine("            if (handler == null) throw new ArgumentNullException(nameof(handler));");
            source.Append("            return global::TrainOP.TrainRouteStationExtensions.")
                .Append(site.CoreMethodName)
                .Append("(route, stationName, handler, global::TrainOP.TrainRouteStationExtensions.")
                .Append(site.BindingFieldName)
                .AppendLine(");");
            source.AppendLine("        }");
            return true;
        }

        private static string Escape(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        /// <summary>
        /// Describes one legacy station call site that should be intercepted.
        /// </summary>
        internal sealed class InterceptorSite
        {
            public InterceptorSite(
                InvocationExpressionSyntax invocation,
                string routeMethodName,
                string handlerTypeName,
                string coreMethodName,
                string bindingFieldName)
            {
                Invocation = invocation;
                RouteMethodName = routeMethodName;
                HandlerTypeName = handlerTypeName;
                CoreMethodName = coreMethodName;
                BindingFieldName = bindingFieldName;
            }

            public InvocationExpressionSyntax Invocation { get; }

            public string RouteMethodName { get; }

            public string HandlerTypeName { get; }

            public string CoreMethodName { get; }

            public string BindingFieldName { get; }
        }
    }
}
