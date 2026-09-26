using DuAnCode.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DuAnCode.Web.Controllers
{
    [Authorize]
    public class AiDashboardController : Controller
    {
        private readonly ApplicationDbContext _db;
        public AiDashboardController(ApplicationDbContext db) { _db = db; }

        public async Task<IActionResult> Index()
        {
            var suggestions = await _db.AiSuggestions.OrderByDescending(a => a.CreatedAt).Take(100).ToListAsync();
            return View(suggestions);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Review(string suggestionId, string action)
        {
            var s = await _db.AiSuggestions.FindAsync(suggestionId);
            if (s == null) { TempData["Error"] = "Gợi ý không tồn tại"; return RedirectToAction("Index"); }
            if (action == "approve") { s.Status = "APPROVED"; s.ReviewedAt = DateTime.UtcNow; s.ReviewedBy = User?.Identity?.Name; }
            else { s.Status = "REJECTED"; s.ReviewedAt = DateTime.UtcNow; s.ReviewedBy = User?.Identity?.Name; }
            await _db.SaveChangesAsync();
            TempData["Message"] = "Cập nhật trạng thái gợi ý.";
            return RedirectToAction("Index");
        }
    }
}
