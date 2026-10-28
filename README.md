# HỆ THỐNG QUẢN LÝ KHO HÀNG NỘI THẤT TÍCH HỢP AI (DuAnCode WMS)

> **Khoa Công nghệ Thông tin - Học phần:** Ứng dụng AI trong Phát triển Phần mềm  
> **Nhóm thực hiện:** Nhóm 7 (Nguyễn Đức Hiếu - Trưởng nhóm, Trần Minh Hiếu)  
> **Kiến trúc:** .NET 9 ASP.NET Core MVC • EF Core 9 • SQL Server • Local LLM Ollama (`qwen2.5-coder:7b`)  
> **Kiểm thử tự động:** 36/36 tests xUnit Passed (100%)

---

## 🚀 HƯỚNG DẪN CHẠY ỨNG DỤNG NHANH

### 1. Chuẩn bị Cơ sở dữ liệu & Cấu hình
Mở file `appsettings.json`, đảm bảo chuỗi kết nối SQL Server hợp lệ:
```json
"ConnectionStrings": {
  "DefaultConnection": "Server=.\\SQLEXPRESS;Database=FurnitureWms;Trusted_Connection=True;TrustServerCertificate=True;"
}
```
*(Hệ thống sử dụng EF Core Migration tự động khởi tạo 32 bảng và nạp sẵn dữ liệu mẫu `DbSeeder.cs` khi khởi chạy lần đầu).*

### 2. Khởi chạy Mô hình AI Local (Ollama)
Cài đặt Ollama và tải mô hình:
```bash
ollama run qwen2.5-coder:7b
```
*(Nếu Ollama chưa chạy, hệ thống có `OllamaStartupService` tự động khởi động tiến trình nền).*

### 3. Chạy Ứng dụng Web
Tại thư mục này, thực thi lệnh:
```bash
dotnet run
```
Truy cập tại trình duyệt: **`http://localhost:5000`** hoặc **`https://localhost:5001`**.

### 4. Chạy Bộ Kiểm thử Tự động (xUnit)
```bash
dotnet test tests/DuAnCode.Tests/DuAnCode.Tests.csproj
```
Kết quả mong đợi: `Passed! - Failed: 0, Passed: 36, Skipped: 0, Total: 36`.

---

## 🔑 DANH SÁCH TÀI KHOẢN TRUY CẬP HỆ THỐNG (RBAC)

| Tài khoản | Mật khẩu | Vai trò (Role) | Chức năng chính |
| :--- | :--- | :--- | :--- |
| **`admin`** | `Admin@123456` | Quản trị viên (Admin) | Quản lý hệ thống, cấu hình kho, người dùng, sao lưu/phục hồi, xem audit log |
| **`giamdoc`** | `Giamdoc@123` | Giám đốc (Director) | Phê duyệt cấp 3 (Phiếu xuất giá trị $> 100.000.000$ VNĐ) |
| **`quanly`** | `Quanly@123` | Quản lý kho (WarehouseManager) | Phê duyệt cấp 2 các phiếu xuất, nhập, chuyển kho; duyệt đề xuất bù hàng AI |
| **`ketoan`** | `Ketoan@123` | Kế toán (Accountant) | Phê duyệt cấp 1; lập và đối soát báo cáo Nhập - Xuất - Tồn |
| **`qc`** | `Qc@123` | Kiểm định chất lượng (QC) | Đánh giá chất lượng hàng khi nhập, gắn nhãn hàng Đạt (GOOD) hoặc Lỗi (DAMAGED) |
| **`thukho`** | `Thukho@123` | Thủ kho / Nhân viên (Staff) | Lập chứng từ nháp, thao tác nhập/xuất kho vật lý, đếm hàng kiểm kê |

---

## 📂 LIÊN KẾT TÀI LIỆU KỸ THUẬT & MINH CHỨNG
- [Kiến trúc Nhà máy Phần mềm AI (AI Software Factory)](agents/README.md)
- [Báo cáo Thực nghiệm So sánh Prompt & Bảo mật](docs/PROMPT_ENGINEERING_AND_SAFETY.md)
- [Bộ 8 tập tài liệu dự án qua 7 giai đoạn SDLC](../CacGiaiDoanThucHien)
- [Tài liệu Đặc tả Yêu cầu Phần mềm (SRS v1.0)](../SRS_Furniture_WMS.md)
- [Tài liệu Yêu cầu Người dùng (URD v2.0)](../URD-WMS-AI-NoiThat-v2.0.md)
