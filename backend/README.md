# Furniture WMS Backend

## Chạy môi trường phát triển

1. Tạo virtual environment và cài `pip install -r requirements.txt`.
2. Sao chép `.env.example` thành `.env`, sau đó thay `WMS_JWT_SECRET_KEY` và chuỗi kết nối SQL Server.
3. Chạy `uvicorn app.main:app --reload`.
4. Mở `/docs` để xem OpenAPI, hoặc chạy `pytest` để kiểm tra nghiệp vụ CBM.
5. Sau khi chạy migration, thực hiện `python scripts/seed_roles.py` để tạo các vai trò chuẩn.

Nếu CSDL đã được tạo trước khi bổ sung luồng QC, chạy
`migrations/sql/001_add_purchase_order_damaged_reason.sql` để thêm trường lý do hàng lỗi.

Không ghi trực tiếp bảng `stock_ledger`: mọi biến động tồn phải đi qua `InventoryService` trong một transaction có khóa pessimistic.
