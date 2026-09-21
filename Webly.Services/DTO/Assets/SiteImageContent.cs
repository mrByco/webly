namespace Webly.Services.DTO.Assets;

/// <summary>
/// One image's bytes, out of the site's own repository.
/// </summary>
/// <param name="Content">The blob at the head commit, exactly as it was uploaded.</param>
/// <param name="ContentType">
/// From the bytes, not from the name. The name is the customer's and the extension came from a sniff when it
/// was uploaded, but serving what a file calls itself is how a file that is not an image gets served as one.
/// </param>
public record SiteImageContent(byte[] Content, string ContentType);
