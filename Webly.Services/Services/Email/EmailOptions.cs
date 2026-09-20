using System.ComponentModel.DataAnnotations;

namespace Webly.Services.Services.Email;

public class EmailOptions
{
    public const string SectionName = "Email";

    [Required]
    public string FromAddress { get; set; } = string.Empty;

    [Required]
    public string FromName { get; set; } = "Webly";

    /// <summary>
    /// The origin links in emails are built from. Deliberately configuration and never the inbound
    /// request's host header: an attacker who can set that header would otherwise get password-reset
    /// links pointed at their own domain, mailed out by us.
    /// </summary>
    [Required]
    public string BaseUrl { get; set; } = string.Empty;

    public ResendOptions Resend { get; set; } = new();

    public class ResendOptions
    {
        public string? ApiKey { get; set; }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
    }
}
