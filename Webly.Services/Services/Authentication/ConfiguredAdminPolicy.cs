using Webly.Data;
using Webly.Data.Repositories.Users;
using Microsoft.Extensions.Options;

namespace Webly.Services.Services.Authentication;

public class ConfiguredAdminPolicy(
    IUserRepository userRepository,
    IOptions<AdministratorOptions> options) : IAdminPolicy
{
    public bool IsAdmin(string email)
    {
        var normalized = NormalizedEmail.From(email);

        return options.Value.Emails.Any(x => NormalizedEmail.From(x) == normalized);
    }

    public async Task<bool> IsAdminAsync(int userId, CancellationToken cancellationToken = default)
    {
        // Nothing to look up if the list is empty, which is the common case.
        if (options.Value.Emails.Count == 0)
            return false;

        var email = await userRepository.FindEmailAsync(userId, cancellationToken);

        return email is not null && IsAdmin(email);
    }
}
