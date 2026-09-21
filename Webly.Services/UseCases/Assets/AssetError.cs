namespace Webly.Services.UseCases.Assets;

public enum AssetError
{
    SiteNotFound,

    /// <summary>Nothing was attached.</summary>
    Empty,

    /// <summary>One of the files is not an image this product will serve. The detail names it.</summary>
    NotAnImage,

    /// <summary>One file, or all of them together, is more than a website should carry.</summary>
    TooLarge,

    /// <summary>No image of that name is in the site.</summary>
    NotFound,

    /// <summary>
    /// A page still uses it. The detail names the files, because "ask the assistant to take it off the home
    /// page" is only actionable if the person knows which page it is on.
    /// </summary>
    InUse
}
