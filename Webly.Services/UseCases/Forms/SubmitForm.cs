using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Webly.Data;
using Webly.Data.Models.Forms;
using Webly.Data.Repositories.Forms;
using Webly.Data.Repositories.Sites;
using Webly.Services.DTO.Common;
using Webly.Services.DTO.Forms;
using Webly.Services.Services;
using Webly.Services.Services.Email;
using Webly.Services.Services.Email.Templates;

namespace Webly.Services.UseCases.Forms;

/// <summary>
/// Takes what a visitor filled in on a published site: stores it, and tells the owner.
///
/// <b>The only use case in Webly with no caller to authorize.</b> Everything else starts from a signed-in
/// person, usually from <see cref="ISiteRepository.FindForOwnerAsync"/>; this one starts from a stranger who
/// loaded a page. So the safety is in what it is unable to do rather than in who is asking: it appends one row
/// and sends one email, it reads nothing back to the caller, and the answer to a valid and an invalid site
/// nanoid is deliberately the same shape of nothing.
///
/// What it does check, and why each one is here rather than left to the rate limiter:
///
/// <list type="bullet">
/// <item><b>The honeypot</b> catches the overwhelming majority of what will actually hit this endpoint, which
/// is a bot walking a form it found. Dropped silently and answered as a success, because an error tells the
/// bot's author which field to leave alone.</item>
/// <item><b>The size caps</b> are what stops this being a way to write megabytes into somebody else's
/// database. A contact form has a handful of fields and a paragraph in the longest one.</item>
/// <item><b>The per-site cap</b> is the one the per-IP rate limiter cannot express: a thousand hosts sending
/// one submission each passes every per-IP limit there is. Two windows, because they protect different
/// things — the day's cap protects our disk and the hour's protects the owner's inbox, and a flood should stop
/// mailing them long before it stops being recorded.</item>
/// </list>
/// </summary>
public class SubmitForm(
    ISiteRepository siteRepository,
    IFormSubmissionRepository submissions,
    IEmailSender emailSender,
    IOptions<AppOptions> app,
    WeblyDbContext dbContext,
    ILogger<SubmitForm> logger)
{
    /// <summary>The most fields one submission may carry. A long contact form asks eight things.</summary>
    public const int MaxFields = 25;

    /// <summary>Per field. Long enough for a paragraph somebody typed, short enough not to be a payload.</summary>
    public const int MaxValueLength = 4000;

    public const int MaxNameLength = 64;

    /// <summary>Everything together, so twenty-five fields of four thousand characters is still refused.</summary>
    public const int MaxTotalLength = 20_000;

    /// <summary>
    /// What one site may take in a day before the endpoint starts refusing. High enough that no small business
    /// will meet it and low enough that a flood cannot fill a disk overnight.
    /// </summary>
    public const int MaxPerDay = 500;

    /// <summary>
    /// How many will be emailed in an hour. Past this the submissions are still stored — the owner can read
    /// them in the editor, and losing a real message because a bot arrived at the same time would be the worse
    /// mistake — but the mail stops, because two hundred emails is not a notification, it is the flood arriving
    /// in a second place.
    /// </summary>
    public const int MaxEmailsPerHour = 25;

    public async Task<Result<FormError>> ExecuteAsync(
        string siteNanoid,
        IReadOnlyList<SubmittedFormField> posted,
        string? submittedFromIp,
        CancellationToken cancellationToken = default)
    {
        var site = await siteRepository.FindForSubmissionAsync(siteNanoid, cancellationToken);

        if (site is null) return Result<FormError>.Fail(FormError.SiteNotFound);

        // Before anything is read into memory beyond what the request already held.
        if (posted.Count > MaxFields
            || posted.Any(x => x.Name.Length > MaxNameLength || x.Value.Length > MaxValueLength)
            || posted.Sum(x => x.Name.Length + x.Value.Length) > MaxTotalLength)
            return Result<FormError>.Fail(FormError.TooLarge);

        var honeypot = posted.FirstOrDefault(x => x.Name == FormFieldNames.Honeypot);

        if (!string.IsNullOrWhiteSpace(honeypot?.Value))
        {
            // A success, on purpose. See the class comment.
            logger.LogInformation("Dropped a form submission for {Site}: the honeypot was filled in.", site.Nanoid);

            return Result<FormError>.Ok();
        }

        var fields = posted
            .Where(x => !FormFieldNames.IsReserved(x.Name) && !string.IsNullOrWhiteSpace(x.Value))
            .Select(x => new SubmittedField { Name = x.Name.Trim(), Value = x.Value.Trim() })
            .ToList();

        // An empty submission is a mis-wired form or a bot probing, and storing it would mean the owner's list
        // is mostly blanks. Not a success: the person who pressed the button deserves to know nothing was sent.
        if (fields.Count == 0) return Result<FormError>.Fail(FormError.Empty);

        var now = DateTime.UtcNow;
        var today = await submissions.CountSinceAsync(site.Id, now.AddDays(-1), cancellationToken);

        if (today >= MaxPerDay)
        {
            logger.LogWarning(
                "Refused a form submission for {Site}: {Count} already today, which is the cap.",
                site.Nanoid,
                today);

            return Result<FormError>.Fail(FormError.TooMany);
        }

        var formName = posted.FirstOrDefault(x => x.Name == FormFieldNames.Form)?.Value;

        submissions.Add(new FormSubmission
        {
            SiteId = site.Id,
            FormName = Name(formName),
            Fields = fields,
            SubmittedFromIp = submittedFromIp
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        await NotifyAsync(site, fields, now, cancellationToken);

        return Result<FormError>.Ok();
    }

    /// <summary>
    /// Emails the owner, unless this site has already had its hour's worth.
    ///
    /// Its own try/catch, and that is the point of it being after the save: the message is already recorded, so
    /// a mail transport having a bad minute must not turn a delivered message into an error page for the
    /// visitor who sent it. The owner still finds it in the editor.
    /// </summary>
    private async Task NotifyAsync(
        Data.Models.Sites.Site site,
        IReadOnlyList<SubmittedField> fields,
        DateTime now,
        CancellationToken cancellationToken)
    {
        try
        {
            var thisHour = await submissions.CountSinceAsync(site.Id, now.AddHours(-1), cancellationToken);

            if (thisHour > MaxEmailsPerHour)
            {
                logger.LogWarning(
                    "Not emailing {Site}'s owner about a submission: {Count} in the last hour.",
                    site.Nanoid,
                    thisHour);

                return;
            }

            var link = $"{app.Value.Origin}/sites/{site.Nanoid}/messages";

            await emailSender.SendAsync(
                WeblyEmails.FormSubmitted(
                    site.Owner.Email,
                    site.Owner.DisplayName,
                    site.Name,
                    [.. fields.Select(x => (x.Name, x.Value))],
                    ReplyTo(fields),
                    link),
                cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception, "A form submission for {Site} was stored but could not be emailed.", site.Nanoid);
        }
    }

    /// <summary>
    /// The visitor's own address, if one of the fields is one, so the owner can press reply and answer them.
    ///
    /// The whole point of a contact form is a conversation, and a notification you cannot reply to makes the
    /// owner copy an address out of an email by hand. Matched on the field's name first — a form that asks for
    /// an email calls it something with "email" or "mail" in it — and then on the value looking like an
    /// address, because a reply-to header built from whatever a stranger typed is worth checking twice.
    /// </summary>
    private static string? ReplyTo(IReadOnlyList<SubmittedField> fields) =>
        fields.FirstOrDefault(x =>
                x.Name.Contains("mail", StringComparison.OrdinalIgnoreCase)
                && x.Value.Length <= 254
                && x.Value.Count(c => c == '@') == 1
                && x.Value.IndexOf('@') > 0
                && x.Value.LastIndexOf('.') > x.Value.IndexOf('@') + 1
                && !x.Value.Any(char.IsWhiteSpace))
            ?.Value;

    /// <summary>
    /// The form's name, cleaned up. A stranger supplies it, so it is trimmed to the column's length and falls
    /// back to the default rather than being trusted: it ends up on the owner's screen.
    /// </summary>
    private static string Name(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? FormSubmission.DefaultFormName
            : value.Trim()[..Math.Min(value.Trim().Length, MaxNameLength)];
}
