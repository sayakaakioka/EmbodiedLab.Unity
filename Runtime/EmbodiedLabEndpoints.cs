#nullable enable

using System;
using EmbodiedLab.Unity.Internal;

namespace EmbodiedLab.Unity
{
    /// <summary>
    /// Network endpoints used by one EmbodiedLab deployment.
    /// </summary>
    public sealed class EmbodiedLabEndpoints
    {
        public EmbodiedLabEndpoints(string apiBaseUrl, string resultWebSocketBaseUrl)
        {
            ApiBaseUri = ParseBaseUri(
                apiBaseUrl,
                nameof(apiBaseUrl),
                "http",
                "https");
            ResultWebSocketBaseUri = ParseBaseUri(
                resultWebSocketBaseUrl,
                nameof(resultWebSocketBaseUrl),
                "ws",
                "wss");
        }

        public Uri ApiBaseUri { get; }

        public Uri ResultWebSocketBaseUri { get; }

        private static Uri ParseBaseUri(
            string value,
            string parameterName,
            params string[] allowedSchemes)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Value cannot be empty.", parameterName);
            }

            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) || uri == null)
            {
                throw new ArgumentException("Value must be an absolute URI.", parameterName);
            }

            return EmbodiedLabTransport.NormalizeBaseUri(
                uri,
                parameterName,
                allowedSchemes);
        }
    }
}
