using System;

namespace TrainOP
{
    /// <summary>
    /// Green signal payload for data-oriented handlers: merged into the manifest by generated adapters.
    /// </summary>
    public sealed class GreenPayload<T> : IGreenPayload
    {
        /// <summary>
        /// Creates a green payload wrapper for the provided value.
        /// </summary>
        public GreenPayload(T value)
        {
            Value = value;
        }

        /// <summary>
        /// Gets the payload value to merge into the manifest.
        /// </summary>
        public T Value { get; }

        /// <summary>
        /// Gets the payload value as an object for merge logic.
        /// </summary>
        object IGreenPayload.GetValue()
        {
            return Value;
        }
    }

    /// <summary>
    /// Internal contract for unwrapping green payload values during manifest merge.
    /// </summary>
    internal interface IGreenPayload
    {
        /// <summary>
        /// Gets the inner payload value.
        /// </summary>
        object GetValue();
    }

    /// <summary>
    /// Red signal request for data-oriented handlers: mapped to <see cref="RedSignal"/> by generated adapters.
    /// </summary>
    public sealed class RedFailure
    {
        /// <summary>
        /// Creates a red failure request with the provided code and message.
        /// </summary>
        public RedFailure(string code, string message)
        {
            Code = code ?? throw new ArgumentNullException(nameof(code));
            Message = message ?? throw new ArgumentNullException(nameof(message));
        }

        /// <summary>
        /// Gets the failure code.
        /// </summary>
        public string Code { get; }

        /// <summary>
        /// Gets the failure message.
        /// </summary>
        public string Message { get; }
    }

    /// <summary>
    /// Pass-through result: manifest is left unchanged and the route continues with a green signal.
    /// </summary>
    public sealed class GreenPass
    {
        /// <summary>
        /// Gets the singleton pass-through instance.
        /// </summary>
        public static GreenPass Instance { get; } = new GreenPass();

        /// <summary>
        /// Creates the singleton pass-through instance.
        /// </summary>
        private GreenPass()
        {
        }
    }
}
