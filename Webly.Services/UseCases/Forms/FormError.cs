namespace Webly.Services.UseCases.Forms;

public enum FormError
{
    SiteNotFound,

    /// <summary>Nothing was filled in, or nothing that was not one of the form's own hidden fields.</summary>
    Empty,

    /// <summary>More fields, longer values or more bytes than a contact form has any reason to carry.</summary>
    TooLarge,

    /// <summary>
    /// This site has taken as many submissions as it is allowed to for now. Answered honestly rather than
    /// swallowed: the cap is high enough that a person reaching it is being flooded, and a visitor who sees an
    /// error will try again or phone instead — which is better than believing a message was delivered.
    /// </summary>
    TooMany
}
