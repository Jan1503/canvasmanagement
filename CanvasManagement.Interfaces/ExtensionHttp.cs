namespace CanvasManagement.Interfaces;

/// <summary>
///     One shared <see cref="HttpClient"/> for all extensions: pooled connections that are recycled every few
///     minutes (DNS changes are picked up), a 15 s timeout and one User-Agent. Do not dispose it and do not
///     change its default headers; pass per-request headers on an <see cref="HttpRequestMessage"/> instead.
/// </summary>
public static class ExtensionHttp
{
    public static HttpClient Client { get; } = Create();

    private static HttpClient Create()
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AutomaticDecompression = System.Net.DecompressionMethods.All
        })
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("verpixeld/1.0");
        return client;
    }
}
