using Microsoft.AspNetCore.Identity;

namespace DuAnCode.Web.Models
{
    public class User : IdentityUser
    {
        public string FullName { get; set; } = string.Empty;
    }
}
