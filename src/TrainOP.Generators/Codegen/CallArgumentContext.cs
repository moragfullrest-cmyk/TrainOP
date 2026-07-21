namespace TrainOP.Generators
{
    /// <summary>
    /// Context for emitting one handler invocation argument from <see cref="Handlers.HandlerCallSlot"/>.
    /// </summary>
    internal readonly struct CallArgumentContext
    {
        public CallArgumentContext(
            bool useNeutralWagonNames,
            string tokenVariable,
            string redVariable,
            string signalIssueExpression)
        {
            UseNeutralWagonNames = useNeutralWagonNames;
            TokenVariable = tokenVariable;
            RedVariable = redVariable;
            SignalIssueExpression = signalIssueExpression;
        }

        public bool UseNeutralWagonNames { get; }

        public string TokenVariable { get; }

        public string RedVariable { get; }

        public string SignalIssueExpression { get; }
    }
}
