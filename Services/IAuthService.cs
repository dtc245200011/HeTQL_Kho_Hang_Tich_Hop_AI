using DuAnCode.Web.Models;

namespace DuAnCode.Web.Services
{
    public interface IAuthService
    {
        Task<User?> ValidateUserAsync(string username, string password);
    }
}
