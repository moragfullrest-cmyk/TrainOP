using Microsoft.CodeAnalysis;
using System.Linq;

namespace TrainOP.Generators
{
    /// <summary>
    /// Determines whether a factory method must be resolved via exported schema.
    /// </summary>
    internal static class FactoryAccessibilityHelper
    {
        /// <summary>
        /// Returns true when the factory is part of the public/export contract and must use schema lookup.
        /// </summary>
        public static bool RequiresSchemaLookup(IMethodSymbol factoryMethod, Compilation compilation)
        {
            if (factoryMethod == null)
            {
                return false;
            }

            switch (factoryMethod.DeclaredAccessibility)
            {
                case Accessibility.Public:
                case Accessibility.Protected:
                case Accessibility.ProtectedOrInternal:
                    return IsExportedFactoryContract(factoryMethod);
                case Accessibility.Internal:
                    return HasInternalsVisibleTo(compilation, factoryMethod.ContainingAssembly);
                default:
                    return false;
            }
        }

        /// <summary>
        /// Returns true when the factory is part of the assembly's exported public API (public method on public types).
        /// </summary>
        public static bool IsExportedFactoryContract(IMethodSymbol factoryMethod)
        {
            if (factoryMethod == null)
            {
                return false;
            }

            if (factoryMethod.DeclaredAccessibility != Accessibility.Public
                && factoryMethod.DeclaredAccessibility != Accessibility.Protected
                && factoryMethod.DeclaredAccessibility != Accessibility.ProtectedOrInternal)
            {
                return false;
            }

            for (var type = factoryMethod.ContainingType; type != null; type = type.ContainingType)
            {
                if (type.DeclaredAccessibility != Accessibility.Public)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Returns true when the factory body can be analyzed in the current compilation.
        /// </summary>
        public static bool IsInlineAnalyzable(IMethodSymbol factoryMethod, Compilation compilation)
        {
            if (factoryMethod == null || RequiresSchemaLookup(factoryMethod, compilation))
            {
                return false;
            }

            foreach (var reference in factoryMethod.DeclaringSyntaxReferences)
            {
                if (reference.SyntaxTree != null
                    && compilation.ContainsSyntaxTree(reference.SyntaxTree))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasInternalsVisibleTo(Compilation compilation, IAssemblySymbol owningAssembly)
        {
            if (compilation.Assembly == null || owningAssembly == null)
            {
                return false;
            }

            if (SymbolEqualityComparer.Default.Equals(compilation.Assembly, owningAssembly))
            {
                return false;
            }

            var consumerName = compilation.AssemblyName;
            if (string.IsNullOrEmpty(consumerName))
            {
                return false;
            }

            return owningAssembly.GetAttributes()
                .Any(attribute =>
                {
                    if (!string.Equals(attribute.AttributeClass?.Name, "InternalsVisibleToAttribute", System.StringComparison.Ordinal))
                    {
                        return false;
                    }

                    if (attribute.ConstructorArguments.Length == 0)
                    {
                        return false;
                    }

                    var visibleName = attribute.ConstructorArguments[0].Value as string;
                    if (string.IsNullOrEmpty(visibleName))
                    {
                        return false;
                    }

                    var commaIndex = visibleName.IndexOf(',');
                    if (commaIndex >= 0)
                    {
                        visibleName = visibleName.Substring(0, commaIndex);
                    }

                    return string.Equals(visibleName.Trim(), consumerName, System.StringComparison.Ordinal);
                });
        }
    }
}
