CREATE TABLE stock_movements (
    movement_id BIGINT IDENTITY(1,1) PRIMARY KEY, sku_id NVARCHAR(100) NOT NULL FOREIGN KEY REFERENCES sku_variants(sku_id),
    warehouse_id NVARCHAR(50) NOT NULL FOREIGN KEY REFERENCES warehouses(warehouse_id), stock_status NVARCHAR(20) NOT NULL,
    quantity_delta INT NOT NULL, movement_type NVARCHAR(30) NOT NULL, reference_type NVARCHAR(50) NOT NULL,
    reference_id NVARCHAR(100) NOT NULL, performed_by NVARCHAR(50) NOT NULL FOREIGN KEY REFERENCES users(user_id), performed_at DATETIME2 DEFAULT SYSDATETIME()
);
CREATE INDEX IX_StockMovement_Report ON stock_movements(warehouse_id, sku_id, stock_status, performed_at);
GO
