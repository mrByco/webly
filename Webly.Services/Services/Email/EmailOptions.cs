using System.ComponentModel.DataAnnotations;

namespace Webly.Services.Services.Email;

public class EmailOptions
{
    public const string SectionName = "Email";

    [Required]
    public string FromAddress { get; set; } = string.Empty;

    [Required]
    public string FromName { get; set; } = "Webly";

    // The origin links in mail are built from used to live here, as Email:BaseUrl. It moved to
    // AppOptions when a published site's contact form needed the same value: a form action and a
    // verification link are one fact about one host, and two settings for it are two things that can
    // disagree.

    public ResendOptions Resend { get; set; } = new();

    public class ResendOptions
    {
        public string? ApiKey { get; set; }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
    }
}
