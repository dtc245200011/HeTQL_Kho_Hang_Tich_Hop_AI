using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

namespace DuAnCode.Web.Services
{
    public static class RoleHelper
    {
        public const string Admin = "Quản trị viên";
        public const string Director = "Giám đốc";
        public const string WarehouseManager = "Quản lý kho";
        public const string Accountant = "Kế toán";
        public const string QC = "Kiểm định chất lượng (QC)";
        public const string WarehouseKeeper = "Thủ kho";
        public const string Staff = "Nhân viên";

        public static readonly List<string> AllVietnameseRoles = new()
        {
            Admin,
            Director,
            WarehouseManager,
            Accountant,
            QC,
            WarehouseKeeper,
            Staff
        };

        private static readonly Dictionary<string, string> EnglishToVn = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Admin", Admin },
            { "Director", Director },
            { "WarehouseManager", WarehouseManager },
            { "Accountant", Accountant },
            { "QC", QC },
            { "WarehouseKeeper", WarehouseKeeper },
            { "Staff", Staff }
        };

        private static readonly Dictionary<string, string> VnToEnglish = new(StringComparer.OrdinalIgnoreCase)
        {
            { Admin, "Admin" },
            { Director, "Director" },
            { WarehouseManager, "WarehouseManager" },
            { Accountant, "Accountant" },
            { QC, "QC" },
            { "QC", "QC" },
            { WarehouseKeeper, "WarehouseKeeper" },
            { Staff, "Staff" }
        };

        public static string ToVietnamese(string? role)
        {
            if (string.IsNullOrWhiteSpace(role)) return string.Empty;
            return EnglishToVn.TryGetValue(role.Trim(), out var vn) ? vn : role.Trim();
        }

        public static string ToEnglish(string? role)
        {
            if (string.IsNullOrWhiteSpace(role)) return string.Empty;
            return VnToEnglish.TryGetValue(role.Trim(), out var en) ? en : role.Trim();
        }

        public static string ToVietnameseList(string? rolesString)
        {
            if (string.IsNullOrWhiteSpace(rolesString)) return string.Empty;
            var parts = rolesString.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return string.Join(", ", parts.Select(ToVietnamese));
        }

        public static bool IsUserInRole(ClaimsPrincipal? user, string? requiredRole)
        {
            if (user == null || string.IsNullOrWhiteSpace(requiredRole)) return false;

            // Admin always has full access
            if (user.IsInRole("Admin") || user.IsInRole(Admin)) return true;

            var en = ToEnglish(requiredRole);
            var vn = ToVietnamese(requiredRole);

            return user.IsInRole(requiredRole) || user.IsInRole(en) || user.IsInRole(vn);
        }
    }
}
