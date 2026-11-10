using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace Crystal.Control.Api.Auth;

public sealed class PasswordHasher
{
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const int MemorySize = 64 * 1024;
    private const int Iterations = 3;
    private const int Parallelism = 2;
    private const string Prefix = "argon2id$v=19$m=65536,t=3,p=2$";

    private static readonly Lazy<string> DummyHash = new(
        () => new PasswordHasher().Hash("Crystal-Control-Dummy-Password-2026"),
        LazyThreadSafetyMode.ExecutionAndPublication);

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = HashPassword(password, salt);
        return $"{Prefix}{Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string encodedHash)
    {
        if (!encodedHash.StartsWith(Prefix, StringComparison.Ordinal)) return false;

        var parts = encodedHash[Prefix.Length..].Split('$', 2);
        if (parts.Length != 2) return false;

        try
        {
            var salt = Convert.FromBase64String(parts[0]);
            var expected = Convert.FromBase64String(parts[1]);
            var actual = HashPassword(password, salt);
            return expected.Length == actual.Length &&
                   CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public void VerifyDummy(string password)
    {
        Verify(password, DummyHash.Value);
    }

    private static byte[] HashPassword(string password, byte[] salt)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = MemorySize,
            Iterations = Iterations,
            DegreeOfParallelism = Parallelism
        };
        return argon2.GetBytes(HashBytes);
    }
}
