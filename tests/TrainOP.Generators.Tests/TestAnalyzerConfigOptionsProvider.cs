using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;

namespace TrainOP.Generators.Tests
{
    /// <summary>
    /// Test double for <see cref="AnalyzerConfigOptionsProvider"/> used by generator tests.
    /// </summary>
    internal sealed class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions _globalOptions;

        public TestAnalyzerConfigOptionsProvider(ImmutableDictionary<string, string> globalOptions)
        {
            _globalOptions = new DictionaryAnalyzerConfigOptions(globalOptions);
        }

        public static TestAnalyzerConfigOptionsProvider ForChainDispatchMode(
            string mode,
            string interceptorsNamespaces = null)
        {
            var options = ImmutableDictionary<string, string>.Empty
                .Add(ChainDispatchModeReader.BuildPropertyKey, mode);

            if (interceptorsNamespaces != null)
            {
                options = options.Add(
                    ChainDispatchModeReader.InterceptorsNamespacesKey,
                    interceptorsNamespaces);
            }

            return new TestAnalyzerConfigOptionsProvider(options);
        }

        public override AnalyzerConfigOptions GlobalOptions => _globalOptions;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _globalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _globalOptions;

        private sealed class DictionaryAnalyzerConfigOptions : AnalyzerConfigOptions
        {
            private readonly ImmutableDictionary<string, string> _options;

            public DictionaryAnalyzerConfigOptions(ImmutableDictionary<string, string> options)
            {
                _options = options ?? ImmutableDictionary<string, string>.Empty;
            }

            public override bool TryGetValue(string key, out string value)
            {
                return _options.TryGetValue(key, out value);
            }
        }
    }
}
