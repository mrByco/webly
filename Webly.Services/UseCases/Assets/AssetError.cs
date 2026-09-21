namespace Webly.Services.UseCases.Assets;

public enum AssetError
{
    SiteNotFound,

    /// <summary>Nothing was attached.</summary>
    Empty,

    /// <summary>One of the files is not an image this product will serve. The detail names it.</summary>
    NotAnImage,

    /// <summary>One file, or all of them together, is more than a website should carry.</summary>
    TooLarge
}
