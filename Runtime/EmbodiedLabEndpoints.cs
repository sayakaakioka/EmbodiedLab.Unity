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
        /// <summary>
        /// Creates normalized API and result-stream base URIs.
        /// </summary>
        /// <param name="apiBaseUrl">
        /// HTTPS for remote deployments; HTTP is allowed only for loopback development.
        /// </param>
        /// <param name="resultWebSocketBaseUrl">
        /// WSS for remote deployments; WS is allowed only for loopback development.
        /// </param>
        /// <exception cref="ArgumentException">
        /// An endpoint is not an absolute base URI, uses an unsupported scheme, or uses
        /// plaintext transport for a non-loopback host.
        /// </exception>
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

        /// <summary>
        /// Gets the normalized HTTPS remote or HTTP loopback API base URI.
        /// </summary>
        public Uri ApiBaseUri { get; }

        /// <summary>
        /// Gets the normalized WSS remote or WS loopback result-stream base URI.
        /// </summary>
        public Uri ResultWebSocketBaseUri { get; }

        private static Uri ParseBaseUri(
            string value,
            string parameterName,
            string plaintextScheme,
            string encryptedScheme)
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
                plaintextScheme,
                encryptedScheme);
        }
    }
}
