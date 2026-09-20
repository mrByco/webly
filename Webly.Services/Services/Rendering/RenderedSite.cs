using System.Text;

namespace Webly.Services.Services.Rendering;

/// <summary>
/// One file of a rendered site. Bytes rather than a string, because the same list carries
/// <c>index.html</c> and, when the asset store lands, the images beside it — and a deployment provider
/// takes one list of files, not two.
/// </summary>
public record RenderedFile(string Path, byte[] Content, string ContentType)
{
    public static RenderedFile Text(string path, string content, string contentType) =>
        new(path, Encoding.UTF8.GetBytes(content), contentType);

    public static RenderedFile Html(string path, string content) =>
        Text(path, content, "text/html; charset=utf-8");
}

/// <summary>
/// Everything a deployment needs, and nothing about how it gets there. This is the seam between
/// "what the site is" and "who hosts it": <see cref="ISiteRenderer"/> produces one of these from a
/// document with no provider in sight, and <c>IDeploymentTarget</c> takes one with no knowledge of
/// sections. A second host is then one class.
/// </summary>
public record RenderedSite(IReadOnlyList<RenderedFile> Files)
{
    public long TotalBytes => Files.Sum(x => (long)x.Content.Length);
}
