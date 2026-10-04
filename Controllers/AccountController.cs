using DuAnCode.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
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
        private readonly IEmailSender _emailSender;

        public AccountController(IAuthService auth, SignInManager<User> signInManager, UserManager<User> userManager, RoleManager<DuAnCode.Web.Models.Role> roleManager, IEmailSender emailSender)
        {
            _auth = auth;
            _signInManager = signInManager;
            _userManager = userManager;
            _roleManager = roleManager;
            _emailSender = emailSender;
        }

        [HttpGet]
        public IActionResult Login(string? returnUrl = null, string? message = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            if (string.Equals(message, "Restored", StringComparison.OrdinalIgnoreCase))
            {
                ViewData["Info"] = "Khôi phục dữ liệu thành công. Vui lòng đăng nhập lại!";
            }
            else if (string.Equals(message, "PasswordResetSuccess", StringComparison.OrdinalIgnoreCase))
            {
                ViewData["Info"] = "Đổi mật khẩu thành công. Bạn có thể đăng nhập bằng mật khẩu mới!";
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
        [HttpGet]
        [AllowAnonymous]
        public IActionResult ForgotPassword()
        {
            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                ModelState.AddModelError("", "Vui lòng nhập tên đăng nhập.");
                return View();
            }

            var user = await _userManager.FindByNameAsync(username);
            if (user == null || string.IsNullOrWhiteSpace(user.Email))
            {
                // Don't reveal that the user does not exist or has no email
                ViewData["Message"] = "Nếu tên đăng nhập hợp lệ và có liên kết Email, một liên kết khôi phục đã được gửi.";
                return View("ForgotPasswordConfirmation");
            }

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var callbackUrl = Url.Action("ResetPassword", "Account", new { token, email = user.Email }, Request.Scheme);

            var emailBody = $"<p>Xin chào <b>{user.FullName ?? user.UserName}</b>,</p>" +
                            $"<p>Bạn đã yêu cầu đặt lại mật khẩu cho tài khoản <b>{user.UserName}</b>.</p>" +
                            $"<p>Vui lòng click vào liên kết dưới đây để đặt lại mật khẩu của bạn:</p>" +
                            $"<p><a href='{callbackUrl}' style='padding: 10px 20px; background-color: #0d6efd; color: #fff; text-decoration: none; border-radius: 5px;'>Đặt lại mật khẩu</a></p>" +
                            $"<p>Nếu bạn không yêu cầu, vui lòng bỏ qua email này.</p>";

            await _emailSender.SendEmailAsync(user.Email, "[Hệ thống Kho] Đặt lại mật khẩu", emailBody);

            ViewData["Message"] = "Nếu tên đăng nhập hợp lệ và có liên kết Email, một liên kết khôi phục đã được gửi.";
            return View("ForgotPasswordConfirmation");
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult ResetPassword(string token, string email)
        {
            if (token == null || email == null)
            {
                ModelState.AddModelError("", "Token không hợp lệ.");
            }
            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(string email, string token, string newPassword, string confirmPassword)
        {
            if (newPassword != confirmPassword)
            {
                ModelState.AddModelError("", "Mật khẩu không khớp.");
                return View();
            }

            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
            {
                // Don't reveal that the user does not exist
                return RedirectToAction("Login", new { message = "PasswordResetSuccess" });
            }

            var result = await _userManager.ResetPasswordAsync(user, token, newPassword);
            if (result.Succeeded)
            {
                return RedirectToAction("Login", new { message = "PasswordResetSuccess" });
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError("", error.Description);
            }
            return View();
        }
    }
}


