using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;

namespace TrainOP.Generators.Models
{
    internal sealed class TerminalWagonSchema
    {
        public TerminalWagonSchema(ImmutableArray<WagonBinding> wagons)
        {
            Wagons = wagons;
            SchemaId = BuildSchemaId(wagons);
        }

        public ImmutableArray<WagonBinding> Wagons { get; }

        public string SchemaId { get; }

        private static string BuildSchemaId(ImmutableArray<WagonBinding> wagons)
        {
            var builder = new StringBuilder();
            for (var i = 0; i < wagons.Length; i++)
            {
                builder.Append('|').Append(wagons[i].Name).Append(':').Append(wagons[i].TypeDisplay);
            }

            using (var sha = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(builder.ToString());
                var hash = sha.ComputeHash(bytes);
                var result = new StringBuilder(8);
                for (var i = 0; i < 4; i++)
                {
                    result.Append(hash[i].ToString("x2"));
                }

                return result.ToString();
            }
        }
    }

    internal sealed class TerminalWagonSchemaComparer : IEqualityComparer<TerminalWagonSchema>
    {
        public static TerminalWagonSchemaComparer Instance { get; } = new TerminalWagonSchemaComparer();

        public bool Equals(TerminalWagonSchema x, TerminalWagonSchema y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x == null || y == null)
            {
                return false;
            }

            if (x.Wagons.Length != y.Wagons.Length)
            {
                return false;
            }

            for (var i = 0; i < x.Wagons.Length; i++)
            {
                if (!string.Equals(x.Wagons[i].Name, y.Wagons[i].Name, StringComparison.Ordinal)
                    || !SymbolEqualityComparer.Default.Equals(x.Wagons[i].TypeSymbol, y.Wagons[i].TypeSymbol))
                {
                    return false;
                }
            }

            return true;
        }

        public int GetHashCode(TerminalWagonSchema obj)
        {
            if (obj == null)
            {
                return 0;
            }

            unchecked
            {
                var hash = 17;
                foreach (var wagon in obj.Wagons)
                {
                    hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(wagon.Name);
                    hash = (hash * 31) + SymbolEqualityComparer.Default.GetHashCode(wagon.TypeSymbol);
                }

                return hash;
            }
        }
    }
}
