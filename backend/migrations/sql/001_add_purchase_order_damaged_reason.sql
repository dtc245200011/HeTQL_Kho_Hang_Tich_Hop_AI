-- Chạy một lần cho CSDL đã được khởi tạo từ SQLQuery1.sql phiên bản cũ.
IF COL_LENGTH('purchase_order_lines', 'damaged_reason') IS NULL
BEGIN
    ALTER TABLE purchase_order_lines
    ADD damaged_reason NVARCHAR(500) NULL;
END;
GO
