#nullable enable

using System;
using System.Net;

namespace EmbodiedLab.Unity.Internal
{
    internal sealed class EmbodiedLabTransportException : Exception
    {
        internal EmbodiedLabTransportException(
            HttpStatusCode statusCode,
            string responseBody,
            Uri requestUri)
            : base($"EmbodiedLab request failed with HTTP {(int)statusCode} at {requestUri}.")
        {
            StatusCode = statusCode;
            ResponseBody = responseBody;
            RequestUri = requestUri;
        }

        internal HttpStatusCode StatusCode { get; }

        internal string ResponseBody { get; }

        internal Uri RequestUri { get; }
    }
}
