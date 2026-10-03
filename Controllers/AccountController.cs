using DuAnCode.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using DuAnCode.Web.Models;

namespace DuAnCode.Web.Controllers
{
    public class AccountController : Controller
    {
        private readonly IAuthService _auth;
        private readonly SignInManager<User> _signInManager;
        private readonly UserManager<User> _userManager;
        private readonly RoleManager<DuAnCode.Web.Models.Role> _roleManager;
        public AccountController(IAuthService auth, SignInManager<User> signInManager, UserManager<User> userManager, RoleManager<DuAnCode.Web.Models.Role> roleManager)
        {
            _auth = auth;
            _signInManager = signInManager;
            _userManager = userManager;
            _roleManager = roleManager;
        }

        [HttpGet]
        public IActionResult Login(string? returnUrl = null, string? message = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            if (string.Equals(message, "Restored", StringComparison.OrdinalIgnoreCase))
            {
                ViewData["Info"] = "Khôi phục dữ liệu thành công. Vui lòng đăng nhập lại!";
            }
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Login(string username, string password, string? returnUrl = null)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                ModelState.AddModelError("", "Tên đăng nhập hoặc mật khẩu không chính xác.");
                return View();
            }

            var result = await _signInManager.PasswordSignInAsync(username, password, isPersistent: false, lockoutOnFailure: false);
            if (result.Succeeded)
            {
                // After successful sign-in, ensure admin role integrity for admin account
                var user = await _userManager.FindByNameAsync(username);
                if (user != null && (string.Equals(user.UserName, "admin", System.StringComparison.OrdinalIgnoreCase) || string.Equals(user.UserName, "System Administrator", System.StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        var inRole = await _userManager.IsInRoleAsync(user, "Admin");
                        if (!inRole)
                        {
                            // ensure Admin role exists
                            var roleExists = await _roleManager.RoleExistsAsync("Admin");
                            if (!roleExists)
                            {
                                var r = new DuAnCode.Web.Models.Role { Id = System.Guid.NewGuid().ToString(), Name = "Admin", NormalizedName = "ADMIN" };
                                await _roleManager.CreateAsync(r);
                            }
                            await _userManager.AddToRoleAsync(user, "Admin");
                            // refresh sign-in to update claims
                            await _signInManager.SignInAsync(user, isPersistent: false);
                        }
                    }
                    catch
                    {
                        // ignore errors and continue to login
                    }
                }

                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                {
                    return LocalRedirect(returnUrl);
                }
                return RedirectToAction("Index", "Dashboard");
            }

            ModelState.AddModelError("", "Tên đăng nhập hoặc mật khẩu không chính xác.");
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> Logout()
        {
            // allow GET redirects to logout (used by JS redirects)
            try { await _signInManager.SignOutAsync(); } catch { await HttpContext.SignOutAsync(); }
            try { HttpContext.Session.Clear(); } catch { }
            return RedirectToAction("Login", "Account");
        }

        [HttpGet]
        public async Task<IActionResult> AccessDenied()
        {
            try { await _signInManager.SignOutAsync(); } catch { try { await HttpContext.SignOutAsync(); } catch { } }
            TempData["Error"] = "Tài khoản của bạn không có quyền truy cập hoặc phiên làm việc đã hết hạn. Vui lòng đăng nhập lại tài khoản Admin!";
            return RedirectToAction("Login", "Account");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LogoutPost()
        {
            try { await _signInManager.SignOutAsync(); } catch { await HttpContext.SignOutAsync(); }
            try { HttpContext.Session.Clear(); } catch { }
            return RedirectToAction("Login", "Account");
        }
        [HttpGet]
        public async Task<IActionResult> Profile()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login");
            return View(user);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Profile(string FullName, string PhoneNumber)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login");
            
            user.FullName = FullName;
            user.PhoneNumber = PhoneNumber;
            var result = await _userManager.UpdateAsync(user);
            if (result.Succeeded)
            {
                TempData["Success"] = "Cập nhật hồ sơ thành công!";
            }
            else
            {
                TempData["Error"] = "Cập nhật thất bại.";
            }
            return RedirectToAction("Profile");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(string OldPassword, string NewPassword, string ConfirmPassword)
        {
            if (NewPassword != ConfirmPassword)
            {
                TempData["Error"] = "Mật khẩu mới không khớp.";
                return RedirectToAction("Profile");
            }
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login");
            
            var result = await _userManager.ChangePasswordAsync(user, OldPassword, NewPassword);
            if (result.Succeeded)
            {
                TempData["Success"] = "Đổi mật khẩu thành công!";
            }
            else
            {
                TempData["Error"] = string.Join(", ", result.Errors.Select(e => e.Description));
            }
            return RedirectToAction("Profile");
        }
    }
}
