namespace Webly.Services.Services.Authentication;

public interface IPasswordHasher
{
    string Hash(string password);

    /// <summary>
    /// Verifies a password against a stored hash. Returns false rather than throwing for a
    /// malformed hash, so a corrupted row denies access instead of 500-ing the login endpoint.
    /// </summary>
    bool Verify(string password, string hash);
}
