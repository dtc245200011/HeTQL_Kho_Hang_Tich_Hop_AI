# KIẾN TRÚC NHÀ MÁY PHÁT TRIỂN PHẦN MỀM BẰNG AI (AI SOFTWARE FACTORY)

> **Dự án:** Hệ thống Quản lý Kho Hàng Nội Thất Tích Hợp AI (DuAnCode WMS)  
> **Tiêu chí đáp ứng:** Tiêu chí 2 (Thang điểm 4.0) - Bảng 1: Thiết kế Agent AI cho quy trình SDLC theo Context Engineering, Factory, Kiểm thử đơn vị & tích hợp, Rào chắn an toàn, Cơ chế bảo mật.

---

## 1. Tổng quan Kiến trúc Nhà máy Phần mềm AI (AI Software Factory)
Trong quy trình phát triển phần mềm truyền thống (SDLC), các vai trò phân tích yêu cầu (BA), kiến trúc sư hệ thống (SA), lập trình viên (Dev), kiểm thử viên (QA) và bảo mật (SecOps) thường làm việc rời rạc, tốn nhiều thời gian chuyển giao.

Dự án áp dụng mô hình **AI Software Factory**, biến quy trình phát triển phần mềm thành một dây chuyền tự động hóa với **5 chuyên gia AI Agent** hoạt động hiệp đồng dưới sự chỉ đạo và kiểm duyệt của con người (*Human-in-the-loop*):

```
                                  ┌─────────────────────────────┐
                                  │   Human Developer / Lead    │
                                  └──────────────┬──────────────┘
                                                 │ Chỉ đạo & Giám sát
                                                 ▼
┌──────────────────┐    Feedback     ┌──────────────────────────┐    Specification   ┌────────────────────────┐
│ RequirementAgent │ ◄────────────── │ System Design / OOD Agent│ ◄───────────────── │  Senior Coding Agent   │
│ (URD / SRS Spec) │                 │ (ERD, Architecture, Lock)│                    │ (Clean Code, EF Core 9)│
└────────┬─────────┘                 └───────────┬──────────────┘                    └───────────┬────────────┘
         │                                       │                                               │
         ▼                                       ▼                                               ▼
┌────────────────────────────────────────────────────────────────────────────────────────────────────────┐
│                                   CONTEXT ENGINEERING & RULES ENGINE                                   │
│                        (Quản lý ngữ cảnh dự án & Quy tắc cố định qua .cursorrules)                     │
└───────────────────────────────────────────────┬────────────────────────────────────────────────────────┘
                                                │
         ┌──────────────────────────────────────┴──────────────────────────────────────┐
         ▼                                                                             ▼
┌──────────────────────────┐                                             ┌──────────────────────────┐
│ QA & Test Engineer Agent │                                             │ Critic & Security Agent  │
│ (36 xUnit Tests / E2E)   │ ◄──────────────── Validated Code ────────── │ (Guardrails, RBAC, Anti) │
└──────────────────────────┘                                             └──────────────────────────┘
```

---

## 2. Danh sách 5 AI Agent trong Nhà máy Phần mềm

| Agent | Thư mục đặc tả | Vai trò trong SDLC | Trọng tâm Context & Rào chắn |
| :--- | :--- | :--- | :--- |
| **1. Requirement Agent** | [`requirement/AGENT.md`](./requirement/AGENT.md) | Phân tích bài toán, làm rõ yêu cầu nghiệp vụ, soạn thảo URD v2.0 và SRS v1.0. | Phỏng vấn làm rõ đặc thù nội thất: CBM, Combo BOM, QC Tốt/Lỗi, quy trình phê duyệt đa cấp. |
| **2. Design & Architect Agent** | [`design/AGENT.md`](./design/AGENT.md) | Thiết kế kiến trúc Clean Architecture, mô hình dữ liệu ERD 32 bảng, giải pháp khóa bi quan. | Thiết kế thuật toán chống Deadlock, chuẩn hóa cấu trúc quan hệ 3NF, ma trận phân quyền RBAC. |
| **3. Senior Coding Agent** | [`coding/AGENT.md`](./coding/AGENT.md) | Hiện thực hóa mã nguồn ASP.NET Core 9 MVC, EF Core 9, tích hợp Local LLM API. | Tuân thủ nghiêm ngặt nguyên tắc **"Không tái phát lỗi cũ" (No-regression)** theo `.cursorrules`. |
| **4. QA & Automated Testing Agent** | [`testing/AGENT.md`](./testing/AGENT.md) | Thiết kế ca kiểm thử chức năng, xây dựng bộ 36 tests tự động xUnit, kiểm thử biên & chịu tải. | Kiểm thử tính toàn vẹn tồn kho, kiểm thử luồng nghiệp vụ phức tạp, kiểm thử thuật toán AI. |
| **5. Critic & Security Agent** | [`critic/AGENT.md`](./critic/AGENT.md) | Rà soát mã nguồn, rà soát lỗ hổng bảo mật (OWASP), thiết lập Guardrails chống AI ảo giác. | Chống Hallucination mã phiếu, rào chắn Anti-CSRF, Audit log, xác thực phân quyền API. |

---

## 3. Quản lý Ngữ cảnh (Context Engineering) & Luật phát triển cố định
Tất cả các Agent đều hoạt động dựa trên bộ luật ngữ cảnh chung thống nhất được lưu trữ tại file [`.cursorrules`](file:///d:/Dai_hoc/nam%203/Ung_Dung_AI/Du%20An/DuAnCode/.cursorrules):
- **Phòng chống lỗi UI Flexbox & Chatbox:** Chống tràn layout, cố định header/footer khung chat AI.
- **Phòng chống lỗi Javascript Crash & CDN Fallback:** Xử lý ngoại lệ `safeParseMarkdown`, bảo vệ `localStorage`.
- **Phòng chống lỗi HTTP 400 Bad Request & 500:** Áp dụng mô hình PRG (Post-Redirect-Get), Anti-Forgery Token trên mọi form.
- **An toàn giao dịch & Khóa bi quan:** Bắt buộc sắp xếp SKU/Kho theo thứ tự tăng dần trước khi khóa (`SELECT ... FOR UPDATE / UPDLOCK, ROWLOCK`) để triệt tiêu hoàn toàn nguy cơ Deadlock.
- **Khởi tạo Null-Safe:** Khởi tạo chuỗi rỗng mặc định để triệt tiêu cảnh báo `CS8618`.

---

## 4. Chu trình Vận hành Hiệp đồng (Collaborative Execution Workflow)
1. **Pha 1 (Làm rõ):** `Requirement Agent` tiếp nhận yêu cầu từ PO, tạo bảng câu hỏi Q&A và sinh bản đặc tả SRS.
2. **Pha 2 (Thiết kế):** `Design Agent` nhận SRS, thiết kế Class Diagram, Database Schema và chỉ dẫn kỹ thuật khóa bi quan.
3. **Pha 3 (Lập trình):** `Coding Agent` hiện thực hóa logic theo chỉ dẫn của `Design Agent` và luật từ `.cursorrules`.
4. **Pha 4 (Phản biện & Bảo mật):** `Critic Agent` thẩm tra code, phát hiện rủi ro (như quên return trong if/else, circular reference khi serialize JSON, nguy cơ hallucination).
5. **Pha 5 (Kiểm thử tự động):** `QA Agent` sinh test cases xUnit, chạy `dotnet test`, bảo đảm **100% Pass** trước khi bàn giao cho con người nghiệm thu.
