using System;

namespace TrainOP
{
    /// <summary>
    /// Base type for data-oriented station return values.
    /// </summary>
    public abstract class StationDataResult
    {
    }

    /// <summary>
    /// Successful data-oriented station result wrapping a payload to merge into the manifest.
    /// </summary>
    public sealed class StationDataOk<T> : StationDataResult, IStationDataOk
    {
        public StationDataOk(T value)
        {
            Value = value;
        }

        public T Value { get; }

        object IStationDataOk.GetValue()
        {
            return Value;
        }
    }

    /// <summary>
    /// Failed data-oriented station result mapped to a red signal by generated adapters.
    /// </summary>
    public sealed class StationDataFail : StationDataResult
    {
        public StationDataFail(string code, string message)
        {
            Code = code ?? throw new ArgumentNullException(nameof(code));
            Message = message ?? throw new ArgumentNullException(nameof(message));
        }

        public string Code { get; }

        public string Message { get; }
    }

    /// <summary>
    /// Pass-through data-oriented station result: manifest is left unchanged and the route continues.
    /// </summary>
    public sealed class StationDataSkip : StationDataResult
    {
        public static StationDataSkip Instance { get; } = new StationDataSkip();

        private StationDataSkip()
        {
        }
    }

    internal interface IStationDataOk
    {
        object GetValue();
    }

    /// <summary>
    /// Factory methods for data-oriented station results.
    /// </summary>
    public static class Data
    {
        public static StationDataOk<T> Ok<T>(T value)
        {
            return new StationDataOk<T>(value);
        }

        public static StationDataFail Fail(string code, string message)
        {
            return new StationDataFail(code, message);
        }

        /// <summary>
        /// Leaves the manifest unchanged and continues the route with a green signal.
        /// </summary>
        public static StationDataSkip Skip()
        {
            return StationDataSkip.Instance;
        }
    }
}
