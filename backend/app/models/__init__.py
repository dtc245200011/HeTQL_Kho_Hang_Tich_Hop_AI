"""Import tất cả ORM model để Alembic nhận metadata."""
from app.models.base import Base
from app.models.business import (
    AiSuggestion,
    BomComponent,
    ComboProduct,
    ProductModel,
    PurchaseOrder,
    PurchaseOrderLine,
    SalesOrder,
    SalesOrderLine,
    SkuVariant,
    StockAdjustment,
    StockTransfer,
    StockTransferLine,
    Supplier,
)
from app.models.core import AuditLog, Role, StockLedger, StockMovement, User, Warehouse

__all__ = [
    "Base", "Role", "User", "Warehouse", "StockLedger", "StockMovement", "AuditLog", "Supplier",
    "ProductModel", "SkuVariant", "ComboProduct", "BomComponent", "PurchaseOrder",
    "PurchaseOrderLine", "SalesOrder", "SalesOrderLine", "StockTransfer",
    "StockTransferLine", "StockAdjustment", "AiSuggestion",
]
