using Microsoft.CodeAnalysis;



namespace TrainOP.Generators.Handlers

{

    /// <summary>

    /// Full handler description resolved once from syntax: input/output schema and failure metadata.

    /// </summary>

    internal sealed class HandlerSchemaResult

    {

        private HandlerSchemaResult(

            HandlerSchemaFailure failure,

            StationHandlerBinding schema,

            Location handlerLocation,

            string stationName)

        {

            Failure = failure;

            Schema = schema;

            HandlerLocation = handlerLocation;

            StationName = stationName;

        }



        /// <summary>True when <see cref="Schema"/> was built successfully.</summary>

        public bool IsSuccess => Failure == HandlerSchemaFailure.None && Schema != null;



        /// <summary>Why resolution failed, or <see cref="HandlerSchemaFailure.None"/> on success.</summary>

        public HandlerSchemaFailure Failure { get; }



        /// <summary>Unified input/output schema for codegen, chain analysis, and diagnostics.</summary>

        public StationHandlerBinding Schema { get; }



        /// <summary>Preferred diagnostic location for the handler expression.</summary>

        public Location HandlerLocation { get; }



        /// <summary>Station name when resolved from an invocation; otherwise null.</summary>

        public string StationName { get; }



        /// <summary>Creates a successful schema resolution result.</summary>

        public static HandlerSchemaResult Success(

            StationHandlerBinding schema,

            Location handlerLocation,

            string stationName = null)

        {

            return new HandlerSchemaResult(

                HandlerSchemaFailure.None,

                schema,

                handlerLocation,

                stationName);

        }



        /// <summary>Creates a failed schema resolution result.</summary>

        public static HandlerSchemaResult Failed(

            HandlerSchemaFailure failure,

            Location handlerLocation = null,

            string stationName = null)

        {

            return new HandlerSchemaResult(

                failure,

                schema: null,

                handlerLocation,

                stationName);

        }

    }

}


