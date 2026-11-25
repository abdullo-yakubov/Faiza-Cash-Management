using FaizaGold.Domain;
using Microsoft.AspNetCore.Identity;

namespace FaizaGold.Api.Infrastructure;

/// <summary>Хеширование паролей (PBKDF2 из ASP.NET Core Identity).</summary>
public sealed class PasswordService
{
    private readonly PasswordHasher<User> _hasher = new();

    public string Hash(User user, string password) => _hasher.HashPassword(user, password);

    public bool Verify(User user, string password) =>
        !string.IsNullOrEmpty(user.PasswordHash) &&
        _hasher.VerifyHashedPassword(user, user.PasswordHash, password) != PasswordVerificationResult.Failed;
}
