using System;

namespace TrainOP
{
    /// <summary>
    /// Green signal payload for data-oriented handlers: merged into the manifest by generated adapters.
    /// </summary>
    public sealed class GreenPayload<T> : IGreenPayload
    {
        public GreenPayload(T value)
        {
            Value = value;
        }

        public T Value { get; }

        object IGreenPayload.GetValue()
        {
            return Value;
        }
    }

    internal interface IGreenPayload
    {
        object GetValue();
    }

    /// <summary>
    /// Red signal request for data-oriented handlers: mapped to <see cref="RedSignal"/> by generated adapters.
    /// </summary>
    public sealed class RedFailure
    {
        public RedFailure(string code, string message)
        {
            Code = code ?? throw new ArgumentNullException(nameof(code));
            Message = message ?? throw new ArgumentNullException(nameof(message));
        }

        public string Code { get; }

        public string Message { get; }
    }

    /// <summary>
    /// Pass-through result: manifest is left unchanged and the route continues with a green signal.
    /// </summary>
    public sealed class GreenPass
    {
        public static GreenPass Instance { get; } = new GreenPass();

        private GreenPass()
        {
        }
    }
}
