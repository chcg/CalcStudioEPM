using System;

namespace CalcStudioEPM.Epm
{
    /// <summary>
    /// Represents an error that occurred while communicating with Oracle EPM Cloud.
    /// Wraps lower-level HTTP or serialization exceptions with a user-friendly message.
    /// </summary>
    public class EpmException : Exception
    {
        /// <summary>HTTP status code if the error originated from an HTTP response, otherwise 0.</summary>
        public int HttpStatusCode { get; }

        public EpmException(string message)
            : base(message)
        {
        }

        public EpmException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        public EpmException(string message, int httpStatusCode)
            : base(message)
        {
            HttpStatusCode = httpStatusCode;
        }

        public EpmException(string message, int httpStatusCode, Exception innerException)
            : base(message, innerException)
        {
            HttpStatusCode = httpStatusCode;
        }
    }
}
