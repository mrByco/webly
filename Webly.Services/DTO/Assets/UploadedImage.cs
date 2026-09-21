namespace Webly.Services.DTO.Assets;

/// <summary>
/// One file as it arrived from the browser, before anything has decided whether it is an image.
/// </summary>
/// <param name="FileName">What the person's computer called it. Used for the name, never for the type.</param>
/// <param name="Content">The bytes. Read into memory: the size cap is what makes that safe.</param>
public record UploadedImage(string FileName, byte[] Content);
