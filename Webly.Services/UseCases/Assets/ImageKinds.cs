namespace Webly.Services.UseCases.Assets;

/// <summary>
/// What an image file actually is, decided from its first few bytes rather than from what it is called or
/// what the browser said it was.
///
/// <b>The extension is the only thing that decides how a static host serves a file</b>, and both the things
/// that usually name it — the upload's <c>Content-Type</c> header and the original file name — come from
/// whoever is uploading. So the bytes are read, the kind is taken from them, and the extension written into
/// the repository is this type's, not theirs. A file called <c>photo.jpg</c> that is really an HTML document
/// would otherwise be served as HTML from the customer's own domain, and from Webly's origin in the preview,
/// which is the same mistake with our cookies next to it.
///
/// <b>SVG is deliberately absent.</b> It is an image everywhere except in what it can contain: script, a
/// foreign object, a remote reference. Serving one from the preview means serving it from Webly's origin,
/// where the person's session lives. When SVG is wanted it arrives with a sanitizer and a decision, not as a
/// fifth line in this list.
/// </summary>
public record ImageKind(string Extension, string ContentType)
{
    public static ImageKind? Of(byte[] content)
    {
        if (Starts(content, [0xFF, 0xD8, 0xFF])) return new("jpg", "image/jpeg");
        if (Starts(content, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])) return new("png", "image/png");
        if (Starts(content, "GIF87a") || Starts(content, "GIF89a")) return new("gif", "image/gif");

        // WEBP is a RIFF container: "RIFF", four bytes of length, then "WEBP". The length is skipped rather
        // than checked, because a file whose header lies about its own length is still not our problem to
        // diagnose — the browser will refuse it and the person will see a broken image, which is honest.
        if (Starts(content, "RIFF") && content.Length >= 12 && Starts(content[8..], "WEBP"))
            return new("webp", "image/webp");

        return null;
    }

    private static bool Starts(byte[] content, byte[] magic) =>
        content.Length >= magic.Length && content.AsSpan(0, magic.Length).SequenceEqual(magic);

    private static bool Starts(byte[] content, string magic) =>
        Starts(content, [.. magic.Select(c => (byte)c)]);
}
