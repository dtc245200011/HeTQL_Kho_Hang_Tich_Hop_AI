using DuAnCode.Web.Data;
using DuAnCode.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DuAnCode.Web.Services
{
    public class AuthService : IAuthService
    {
        private readonly ApplicationDbContext _db;
        private readonly PasswordHasher<User> _hasher = new PasswordHasher<User>();
        public AuthService(ApplicationDbContext db) { _db = db; }

        public async Task<User?> ValidateUserAsync(string username, string password)
        {
            // The project uses IdentityUser-derived User which exposes 'UserName' and 'Id'.
            var user = await _db.Users.FirstOrDefaultAsync(u => u.UserName == username);
            if (user == null) return null;
            if (string.IsNullOrEmpty(user.PasswordHash)) return null;
            var ver = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
            return ver == PasswordVerificationResult.Success ? user : null;
        }
    }
}
