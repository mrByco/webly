namespace Webly.Services.Services.Authentication;

/// <summary>
/// BCrypt in its "enhanced" mode, which SHA-384-prehashes the input. Plain bcrypt silently
/// truncates at 72 bytes, so without the prehash a long passphrase is weaker than it looks and two
/// different long passwords can collide.
/// </summary>
public class BcryptPasswordHasher : IPasswordHasher
{
    public string Hash(string password) => BCrypt.Net.BCrypt.EnhancedHashPassword(password);

    public bool Verify(string password, string hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.EnhancedVerify(password, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }
}
