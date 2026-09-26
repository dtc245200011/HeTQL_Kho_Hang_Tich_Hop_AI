-- ============================================================================
-- SYSTEM: FURNITURE WMS WITH AI INTEGRATION
-- TARGET DATABASE: Microsoft SQL Server 2019 / 2022
-- FILE: 01_schema.sql
-- ============================================================================

CREATE DATABASE FurnitureWMS;
GO

USE FurnitureWMS;
GO

-- 1. BANG PHAN QUYEN & NGUOI DUNG
CREATE TABLE roles (
    role_id NVARCHAR(50) PRIMARY KEY,
    role_name NVARCHAR(100) NOT NULL
);

CREATE TABLE users (
    user_id NVARCHAR(50) PRIMARY KEY,
    username NVARCHAR(50) NOT NULL UNIQUE,
    password_hash NVARCHAR(255) NOT NULL,
    full_name NVARCHAR(100) NOT NULL,
    role_id NVARCHAR(50) NOT NULL FOREIGN KEY REFERENCES roles(role_id),
    is_active BIT DEFAULT 1,
    created_at DATETIME2 DEFAULT SYSDATETIME()
);

-- 2. BANG NHA CUNG CAP & KHO
CREATE TABLE suppliers (
    supplier_id NVARCHAR(50) PRIMARY KEY,
    tax_code NVARCHAR(50) NOT NULL UNIQUE,
    name NVARCHAR(200) NOT NULL,
    address NVARCHAR(500),
    email NVARCHAR(100),
    phone NVARCHAR(20),
    is_active BIT DEFAULT 1
);

CREATE TABLE warehouses (
    warehouse_id NVARCHAR(50) PRIMARY KEY,
    warehouse_name NVARCHAR(100) NOT NULL,
    warehouse_type NVARCHAR(50) CHECK (warehouse_type IN (N'KHO_CHINH', N'KHO_CHI_NHANH', N'KHO_HANG_LOI')),
    max_capacity_cbm DECIMAL(18, 4) NOT NULL CHECK (max_capacity_cbm > 0),
    is_active BIT DEFAULT 1
);

-- 3. BANG MASTER DATA HANG HOA & BIEN THE (CBM & SKU)
CREATE TABLE product_models (
    product_model_id NVARCHAR(50) PRIMARY KEY,
    product_name NVARCHAR(200) NOT NULL,
    category NVARCHAR(100) NOT NULL,
    unit NVARCHAR(20) NOT NULL,
    min_stock INT DEFAULT 0 CHECK (min_stock >= 0),
    is_active BIT DEFAULT 1,
    created_at DATETIME2 DEFAULT SYSDATETIME()
);

CREATE TABLE sku_variants (
    sku_id NVARCHAR(100) PRIMARY KEY, -- Ma: {model_id}-{color}-{material}
    product_model_id NVARCHAR(50) NOT NULL FOREIGN KEY REFERENCES product_models(product_model_id),
    color NVARCHAR(50) NOT NULL,
    material NVARCHAR(50) NOT NULL,
    length_mm INT NOT NULL CHECK (length_mm > 0),
    width_mm INT NOT NULL CHECK (width_mm > 0),
    height_mm INT NOT NULL CHECK (height_mm > 0),
    cbm DECIMAL(18, 6) NOT NULL, -- Tinh: (D x R x C) / 1,000,000,000
    unit_price DECIMAL(18, 2) NOT NULL DEFAULT 0,
    is_active BIT DEFAULT 1,
    CONSTRAINT UQ_Variant UNIQUE (product_model_id, color, material)
);

-- 4. BANG HANG COMBO / BOM
CREATE TABLE combo_products (
    combo_id NVARCHAR(50) PRIMARY KEY,
    combo_name NVARCHAR(200) NOT NULL,
    cbm_override DECIMAL(18, 6) NULL,
    min_stock INT DEFAULT 0
);

CREATE TABLE bom_components (
    combo_id NVARCHAR(50) NOT NULL FOREIGN KEY REFERENCES combo_products(combo_id),
    sku_id NVARCHAR(100) NOT NULL FOREIGN KEY REFERENCES sku_variants(sku_id),
    quantity_per_set INT NOT NULL CHECK (quantity_per_set > 0),
    PRIMARY KEY (combo_id, sku_id)
);

-- 5. BANG TON KHO RANG BUOC LOCKING & TRANG THAI (GOOD/DAMAGED)
CREATE TABLE stock_ledger (
    sku_id NVARCHAR(100) NOT NULL FOREIGN KEY REFERENCES sku_variants(sku_id),
    warehouse_id NVARCHAR(50) NOT NULL FOREIGN KEY REFERENCES warehouses(warehouse_id),
    status NVARCHAR(20) NOT NULL CHECK (status IN ('GOOD', 'DAMAGED')),
    quantity INT NOT NULL DEFAULT 0 CHECK (quantity >= 0),
    updated_at DATETIME2 DEFAULT SYSDATETIME(),
    PRIMARY KEY (sku_id, warehouse_id, status)
);

-- 6. BANG PHIEU NHAP KHO (INBOUND)
CREATE TABLE purchase_orders (
    po_id NVARCHAR(50) PRIMARY KEY,
    po_date DATE NOT NULL,
    supplier_id NVARCHAR(50) NOT NULL FOREIGN KEY REFERENCES suppliers(supplier_id),
    warehouse_id NVARCHAR(50) NOT NULL FOREIGN KEY REFERENCES warehouses(warehouse_id),
    status NVARCHAR(20) NOT NULL CHECK (status IN ('DRAFT', 'PENDING_L1', 'PENDING_L2', 'APPROVED', 'CANCELLED')),
    total_amount DECIMAL(18, 2) NOT NULL DEFAULT 0,
    created_by NVARCHAR(50) NOT NULL FOREIGN KEY REFERENCES users(user_id),
    created_at DATETIME2 DEFAULT SYSDATETIME()
);

CREATE TABLE purchase_order_lines (
    po_id NVARCHAR(50) NOT NULL FOREIGN KEY REFERENCES purchase_orders(po_id),
    sku_id NVARCHAR(100) NOT NULL FOREIGN KEY REFERENCES sku_variants(sku_id),
    qty_document INT NOT NULL CHECK (qty_document > 0),
    qty_actual_good INT DEFAULT 0 CHECK (qty_actual_good >= 0),
    qty_actual_damaged INT DEFAULT 0 CHECK (qty_actual_damaged >= 0),
    unit_cost DECIMAL(18, 2) NOT NULL,
    variance_reason NVARCHAR(500) NULL,
    damaged_reason NVARCHAR(500) NULL,
    PRIMARY KEY (po_id, sku_id)
);

-- Immutable movement ledger: nguồn dữ liệu cho báo cáo Nhập-Xuất-Tồn theo kỳ.
CREATE TABLE stock_movements (
    movement_id BIGINT IDENTITY(1,1) PRIMARY KEY,
    sku_id NVARCHAR(100) NOT NULL FOREIGN KEY REFERENCES sku_variants(sku_id),
    warehouse_id NVARCHAR(50) NOT NULL FOREIGN KEY REFERENCES warehouses(warehouse_id),
    stock_status NVARCHAR(20) NOT NULL CHECK (stock_status IN ('GOOD', 'DAMAGED')),
    quantity_delta INT NOT NULL,
    movement_type NVARCHAR(30) NOT NULL,
    reference_type NVARCHAR(50) NOT NULL,
    reference_id NVARCHAR(100) NOT NULL,
    performed_by NVARCHAR(50) NOT NULL FOREIGN KEY REFERENCES users(user_id),
    performed_at DATETIME2 DEFAULT SYSDATETIME()
);
CREATE INDEX IX_StockMovement_Report ON stock_movements(warehouse_id, sku_id, stock_status, performed_at);

-- 7. BANG PHIEU XUAT KHO (OUTBOUND)
CREATE TABLE sales_orders (
    so_id NVARCHAR(50) PRIMARY KEY,
    so_date DATE NOT NULL,
    warehouse_id NVARCHAR(50) NOT NULL FOREIGN KEY REFERENCES warehouses(warehouse_id),
    status NVARCHAR(20) NOT NULL CHECK (status IN ('DRAFT', 'PENDING_L1', 'PENDING_L2', 'PENDING_L3', 'APPROVED', 'CANCELLED')),
    total_amount DECIMAL(18, 2) NOT NULL DEFAULT 0,
    created_by NVARCHAR(50) NOT NULL FOREIGN KEY REFERENCES users(user_id),
    created_at DATETIME2 DEFAULT SYSDATETIME()
);

CREATE TABLE sales_order_lines (
    line_id INT IDENTITY(1,1) PRIMARY KEY,
    so_id NVARCHAR(50) NOT NULL FOREIGN KEY REFERENCES sales_orders(so_id),
    sku_id NVARCHAR(100) NULL FOREIGN KEY REFERENCES sku_variants(sku_id),
    combo_id NVARCHAR(50) NULL FOREIGN KEY REFERENCES combo_products(combo_id),
    qty INT NOT NULL CHECK (qty > 0),
    unit_price DECIMAL(18, 2) NOT NULL
);

-- 8. BANG CHUYEN KHO & KIEM KE & DIEU CHINH TON
CREATE TABLE stock_transfers (
    transfer_id NVARCHAR(50) PRIMARY KEY,
    from_warehouse_id NVARCHAR(50) NOT NULL FOREIGN KEY REFERENCES warehouses(warehouse_id),
    to_warehouse_id NVARCHAR(50) NOT NULL FOREIGN KEY REFERENCES warehouses(warehouse_id),
    status NVARCHAR(20) CHECK (status IN ('DRAFT', 'PENDING', 'APPROVED', 'CANCELLED')),
    created_by NVARCHAR(50) NOT NULL FOREIGN KEY REFERENCES users(user_id),
    created_at DATETIME2 DEFAULT SYSDATETIME()
);

CREATE TABLE stock_transfer_lines (
    transfer_id NVARCHAR(50) NOT NULL FOREIGN KEY REFERENCES stock_transfers(transfer_id),
    sku_id NVARCHAR(100) NOT NULL FOREIGN KEY REFERENCES sku_variants(sku_id),
    qty INT NOT NULL CHECK (qty > 0),
    PRIMARY KEY (transfer_id, sku_id)
);

CREATE TABLE stock_adjustments (
    adjustment_id NVARCHAR(50) PRIMARY KEY,
    sku_id NVARCHAR(100) NOT NULL FOREIGN KEY REFERENCES sku_variants(sku_id),
    warehouse_id NVARCHAR(50) NOT NULL FOREIGN KEY REFERENCES warehouses(warehouse_id),
    qty_delta INT NOT NULL, -- Am: giam ton, Duong: tang ton
    reason NVARCHAR(500) NOT NULL,
    approved_by NVARCHAR(50) NOT NULL FOREIGN KEY REFERENCES users(user_id),
    created_at DATETIME2 DEFAULT SYSDATETIME()
);

-- 9. BANG LOG AI & AUDIT LOG (LƯU TỐI THIỂU 5 NĂM)
CREATE TABLE ai_suggestions (
    suggestion_id NVARCHAR(50) PRIMARY KEY,
    suggestion_type NVARCHAR(50) CHECK (suggestion_type IN ('REPLENISHMENT', 'ANOMALY', 'BOTTLENECK')),
    sku_id NVARCHAR(100) NULL FOREIGN KEY REFERENCES sku_variants(sku_id),
    payload_json NVARCHAR(MAX) NOT NULL,
    status NVARCHAR(20) DEFAULT 'PENDING_REVIEW' CHECK (status IN ('PENDING_REVIEW', 'APPROVED', 'REJECTED')),
    reviewed_by NVARCHAR(50) NULL FOREIGN KEY REFERENCES users(user_id),
    created_at DATETIME2 DEFAULT SYSDATETIME(),
    reviewed_at DATETIME2 NULL
);

CREATE TABLE audit_logs (
    log_id BIGINT IDENTITY(1,1) PRIMARY KEY,
    entity_type NVARCHAR(50) NOT NULL,
    entity_id NVARCHAR(100) NOT NULL,
    action NVARCHAR(20) NOT NULL, -- CREATE, UPDATE, DELETE, APPROVE
    before_json NVARCHAR(MAX) NULL,
    after_json NVARCHAR(MAX) NULL,
    performed_by NVARCHAR(50) NOT NULL FOREIGN KEY REFERENCES users(user_id),
    performed_at DATETIME2 DEFAULT SYSDATETIME(),
    reason NVARCHAR(500) NULL
);

-- INDEX TAP TRUNG TANG TOC CHUYEN MUC TRA CUU TON KHO & AUDIT LOG
CREATE INDEX IX_StockLedger_Search ON stock_ledger(warehouse_id, status) INCLUDE (quantity);
CREATE INDEX IX_AuditLog_Search ON audit_logs(entity_type, entity_id, performed_at);
GO
