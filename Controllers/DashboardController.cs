using DuAnCode.Web.Data;
using DuAnCode.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DuAnCode.Web.Controllers
{
    // Requires authenticated user
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly ApplicationDbContext _db;
        public DashboardController(ApplicationDbContext db) { _db = db; }

        public IActionResult Index()
        {
            // Redirect to Home/Index to unify landing page and avoid duplicate views
            return RedirectToAction("Index", "Home");
        }
    }
}
