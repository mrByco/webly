namespace Webly.Api.Infrastructure;

/// <summary>
/// The outbound client the preview proxy forwards with.
///
/// A bare <see cref="HttpMessageInvoker"/> rather than an <c>HttpClient</c>, and YARP does not merely prefer
/// that — <c>IHttpForwarder.SendAsync</c> throws <c>ArgumentException</c> on an <c>HttpClient</c>, because
/// <c>HttpClient</c> layers a total-request timeout, redirect following and buffering over the handler, and all
/// three are wrong for a proxy that streams somebody else's response and holds a WebSocket open. The preview
/// was written with <c>IHttpClientFactory.CreateClient</c> and answered 500 to every request until the first
/// one was actually made.
///
/// A singleton, which is the shape <c>IHttpClientFactory</c> normally exists to prevent: its handler never
/// rotates, so a DNS change is never picked up. That objection does not apply here. A sandbox is reached at a
/// literal address on a port the provider allocated — nothing to resolve — and pooling connections across the
/// life of the process is exactly what a preview wants, since one open page is a long series of requests to
/// the same dev server.
///
/// The settings each answer a specific failure:
///
/// <list type="bullet">
/// <item>No <c>UseProxy</c>: the destination is a loopback or private address, and an ambient proxy variable
/// pointing this at the internet would be a confusing way to fail.</item>
/// <item>No <c>AllowAutoRedirect</c>: a redirect the site issues belongs to the browser, so that the address
/// bar and the app's own routing see it. Followed here it would silently become a different page.</item>
/// <item>No <c>AutomaticDecompression</c>: the response is relayed, not read. Decompressing it only to send it
/// on costs a copy and invalidates the headers that describe it.</item>
/// <item>No response-body buffering, via <c>ActivityTimeout</c> on the forwarder instead of a deadline here:
/// hot reload is a socket that is quiet for minutes at a time and must not be a timeout.</item>
/// </list>
/// </summary>
public sealed class PreviewForwarder : IDisposable
{
    public HttpMessageInvoker Client { get; } = new(new SocketsHttpHandler
    {
        UseProxy = false,
        AllowAutoRedirect = false,
        AutomaticDecompression = System.Net.DecompressionMethods.None,
        UseCookies = false,
        ConnectTimeout = TimeSpan.FromSeconds(15),

        // Long enough that an idle editor keeps its connections, short enough that a stopped sandbox's are not
        // held for ever — a workspace is reaped after ten minutes of silence.
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2)
    });

    public void Dispose() => Client.Dispose();
}
