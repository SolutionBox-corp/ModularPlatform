using Isopoh.Cryptography.Argon2;

namespace ModularPlatform.Identity.Security;

internal interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string hash, string password);
}

/// <summary>Argon2id password hashing (battle-tested; never roll your own).</summary>
internal sealed class Argon2PasswordHasher : IPasswordHasher
{
    public string Hash(string password) => Argon2.Hash(password);

    public bool Verify(string hash, string password) => Argon2.Verify(hash, password);
}
