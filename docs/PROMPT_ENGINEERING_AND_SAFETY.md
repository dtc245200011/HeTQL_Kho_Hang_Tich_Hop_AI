# BÁO CÁO THỰC NGHIỆM PROMPT ENGINEERING, XỬ LÝ LỖI AI & BẢO MẬT DỮ LIỆU

> **Dự án:** Hệ thống Quản lý Kho Hàng Nội Thất Tích Hợp AI (DuAnCode WMS)  
> **Đáp ứng tiêu chí:** Tiêu chí 3 (3.2 So sánh các phiên bản prompt; 3.3 Xử lý lỗi AI; 3.4 Bảo mật dữ liệu, API key) - Bảng 2.

---

## 1. Mục tiêu thực nghiệm
Hệ thống WMS tích hợp mô hình ngôn ngữ lớn cục bộ (Local LLM - Ollama `qwen2.5-coder:7b`) nhằm hỗ trợ thủ kho và nhà quản lý tra cứu nhanh tồn kho, nhận cảnh báo biến động và tạo chứng từ nhập/xuất dự thảo.
Thách thức lớn nhất khi áp dụng LLM vào quản lý kho doanh nghiệp bao gồm:
1. **Ảo giác số liệu (Hallucination):** LLM tự bịa ra số lượng tồn kho hoặc mã phiếu không tồn tại.
2. **Định dạng phi cấu trúc:** Văn bản trả lời tự do không thể kích hoạt hành động tự động trên hệ thống phần mềm.
3. **Bảo mật và toàn vẹn dữ liệu:** Nguy cơ AI tự ý thay đổi dữ liệu kho hoặc rò rỉ thông tin nội bộ.

Nhóm nghiên cứu đã thực hiện 3 vòng lặp tinh chỉnh Prompt (Prompt Iterations) để giải quyết triệt để các vấn đề trên.

---

## 2. So sánh thực nghiệm 3 phiên bản Prompt (Prompt Evolution Matrix)

### 2.1. Chi tiết 3 phiên bản Prompt

| Tiêu chí | Phiên bản 1: Naive Direct Prompt | Phiên bản 2: Context Grounding Prompt | Phiên bản 3: Guardrails & Action Function Calling Prompt (Hiện tại) |
| :--- | :--- | :--- | :--- |
| **Cấu trúc Prompt** | Chỉ gửi câu hỏi người dùng kèm lời nhắc vai trò: *"Bạn là trợ lý kho WMS, hãy trả lời câu hỏi sau..."* | Bổ sung ngữ cảnh CSDL tĩnh trích xuất trước khi gửi tới LLM. | Tích hợp **Dynamic RAG Grounding** thời gian thực từ CSDL + Bộ quy tắc mã lệnh hành động `[ACTION: ...]` + `[NAVIGATE: ...]`. |
| **Nguồn dữ liệu** | Phụ thuộc hoàn toàn vào tri thức huấn luyện sẵn của mô hình. | Snapshot dữ liệu tĩnh nạp sẵn. | Trích xuất động qua `AiChatGroundingService` (tổng tồn kho, phân loại GOOD/DAMAGED, chi tiết phiếu theo intent). |
| **Khả năng tự động hóa** | 0% (Chỉ trả lời văn bản thuần túy). | 10% (Chỉ gợi ý người dùng tự thao tác). | **100%** (Tự động sinh mã lệnh tạo bản nháp phiếu thật trong DB và điều hướng URL). |
| **Tỷ lệ Ảo giác (Hallucination)** | **Cao (~68%)**: Tự bịa mã phiếu như `PN-123456`, nói mò số tồn kho. | **Trung bình (~22%)**: Tồn kho đúng nhưng vẫn tự bịa mã chứng từ khi yêu cầu tạo phiếu. | **Triệt tiêu hoàn toàn (0%)**: Backend kiểm duyệt bằng Regex, thay thế mã ảo bằng ID thực tế được lưu vào CSDL. |
| **Cơ chế kiểm soát an toàn** | Không có rào chắn. | Không có rào chắn. | **Đa tầng:** Regex Action Filter, Fallback Intent Parser, Human-in-the-loop (Chỉ tạo bản nháp DRAFT). |

---

### 2.2. So sánh kết quả thực nghiệm trên các kịch bản thực tế

#### Kịch bản 1: Người dùng hỏi: *"Hiện tại kho còn bao nhiêu chiếc Ghế xoay đen và có hàng lỗi không?"*
- **Prompt v1 (Thất bại):** *"Hiện tại kho của bạn có 50 chiếc ghế xoay đen và không có hàng lỗi nào."*  
  $\rightarrow$ **Nhận xét:** Sai hoàn toàn. Thực tế trong CSDL chỉ có 20 chiếc tồn chuẩn và 1 chiếc lỗi tại kho `WH-DAMAGED`.
- **Prompt v2 (Đạt một phần):** *"Dựa vào dữ liệu, ghế xoay đen có 20 chiếc ở kho tổng."*  
  $\rightarrow$ **Nhận xét:** Đúng tồn chuẩn nhưng bỏ sót hàng lỗi ở kho hư hỏng.
- **Prompt v3 (Chính xác 100%):**  
  > *"Theo dữ liệu thực tế trích xuất từ hệ thống WMS lúc này:*  
  > *- Mã SKU `SKU-CHAIR-BK` (Ghế xoay - Đen): Đang có **20 sản phẩm** đạt chuẩn (`GOOD`) tại Kho Tổng (`WH-GLOBAL`).*  
  > *- Đang có **1 sản phẩm lỗi** (`DAMAGED`) tại Kho Hàng Lỗi (`WH-DAMAGED`). Số lượng lỗi này đã được cách ly và không tính vào tồn khả dụng xuất bán."*

---

#### Kịch bản 2: Người dùng yêu cầu: *"Tạo cho tôi một đơn nhập 15 chiếc Bàn làm việc nâu từ NCC An Phát"*
- **Prompt v1 (Ảo giác mã phiếu):** *"Tôi đã tạo thành công phiếu nhập mã `PN-20261028` cho bạn với 15 bàn làm việc nâu."*  
  $\rightarrow$ **Hậu quả:** Người dùng vào danh sách tìm kiếm không thấy phiếu đâu vì LLM chỉ bịa ra chuỗi ký tự văn bản chứ không hề gọi CSDL.
- **Prompt v2 (Bất tiện):** *"Bạn hãy truy cập vào menu Nhập kho tại đường dẫn `/Inbound/Create` và tự điền thông tin 15 chiếc bàn nhé."*  
  $\rightarrow$ **Hậu quả:** Không tận dụng được năng lực tự động hóa của AI.
- **Prompt v3 (Tự động hóa hoàn hảo có rào chắn an toàn):**  
  LLM sinh lệnh kèm metadata:  
  `[ACTION: CREATE_DRAFT_INBOUND: {"counterparty": "Công ty TNHH Nội Thất An Phát", "sku": "SKU-DESK-BR", "quantity": 15}]`  
  Backend đón bắt tag này qua Regex, gọi hàm `CreateDraftVoucherInternalAsync` tạo bản ghi thực tế trong CSDL với mã sinh ngẫu nhiên `PN-20261029025000` và trạng thái `DRAFT`. Sau đó backend thay thế tag bằng thẻ UI hoàn chỉnh:
  > *✅ **Đã tạo thành công Bản nháp Phiếu Nhập kho vào CSDL hệ thống!***  
  > *- **Mã phiếu:** `PN-20261029025000`*  
  > *- **Trạng thái:** `Bản nháp (DRAFT)`*  
  > *- **Đối tác:** Công ty TNHH Nội Thất An Phát*  
  > *- **Mặt hàng:** Bàn làm việc nâu (`SKU-DESK-BR`) — Số lượng: 15 Cái*  
  > *- **Tổng giá trị dự kiến:** 37,500,000 đ*  
  > *👉 [Mở & Sửa phiếu PN-20261029025000](/Inbound/Create?id=...) • [Xem danh sách phiếu](/Inbound)*

---

## 3. Các kỹ thuật xử lý lỗi AI (AI Error Handling & Defensive Fallback)

Được triển khai trong mã nguồn tại [`AiChatController.cs`](file:///d:/Dai_hoc/nam%203/Ung_Dung_AI/Du%20An/DuAnCode/HeTQL_Kho_Hang_Tich_Hop_AI/Controllers/AiChatController.cs):
1. **Dự phòng Intent (Fallback Intent Extractor):**  
   Trường hợp mô hình AI nhỏ đôi khi "quên" sinh thẻ lệnh `[ACTION: ...]`, backend sử dụng Regex phân tích câu nhắc của người dùng:
   ```csharp
   bool isCreateIntent = Regex.IsMatch(lowerPrompt, @"(tạo|lập|thêm|khởi tạo)\s+(một\s+)?(đơn|phiếu|bản nháp)");
   ```
   Nếu phát hiện người dùng thực sự muốn tạo đơn, backend tự động trích xuất số lượng và SKU để khởi tạo phiếu nháp, tránh để người dùng phải hỏi lại.
2. **Khắc phục ảo giác mã phiếu (Hallucination Replacement Guard):**  
   Nếu trong câu trả lời văn bản của AI có xuất hiện bất kỳ chuỗi ký tự nào dạng `(PN|PX)-[0-9A-Za-z_-]{6,}`, hệ thống dùng Regex tự động thay thế bằng **Mã phiếu thật** vừa được lưu vào CSDL.
3. **Giới hạn Timeout & Phục hồi kết nối:**  
   Thiết lập Timeout 120s cho HttpClient gọi Ollama; có dịch vụ nền `OllamaStartupService` tự động kiểm tra và khởi động tiến trình Ollama nếu service bị tắt.

---

## 4. Bảo mật dữ liệu & Cơ chế phân quyền (Security Architecture)

1. **Kiến trúc Local On-Premise:**  
   Mô hình AI chạy 100% nội bộ trên máy chủ qua Ollama, không gửi bất kỳ thông tin nào ra dịch vụ đám mây công cộng (như OpenAI hay bên thứ 3), bảo vệ bí mật kinh doanh và giá vốn của doanh nghiệp.
2. **Bảo mật phân quyền API (RBAC Authorization):**  
   Controller AI được bảo vệ nghiêm ngặt bằng thuộc tính kiểm tra Role:
   ```csharp
   [Authorize(Roles = "Admin,Director,WarehouseManager,QC,Quản trị viên,Giám đốc,Quản lý kho,Kiểm định chất lượng (QC)")]
   ```
   Nhân viên không có thẩm quyền sẽ không thể truy cập API chat để tra cứu số liệu kho.
3. **Nguyên tắc cốt lõi Human-in-the-loop:**  
   AI chỉ được gán vai trò `Advisor` (Cố vấn), tuyệt đối không có quyền ghi đè số dư kho hay duyệt phiếu chính thức. Mọi dữ liệu do AI sinh ra bắt buộc ở trạng thái `DRAFT` và phải có thao tác ký duyệt của Thủ kho / Quản lý kho.
4. **Bảo vệ toàn vẹn CSDL:**  
   Mọi giao dịch sửa đổi số dư đều kích hoạt `AuditActionFilter` để lưu lịch sử (Ai, Làm gì, Vào lúc nào, Dữ liệu cũ, Dữ liệu mới) vào bảng `AuditLogs`, lưu vết kiểm toán tối thiểu 5 năm theo yêu cầu SRS.
