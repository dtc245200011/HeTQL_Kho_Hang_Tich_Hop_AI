"""ORM cho các bảng nghiệp vụ còn lại trong SQLQuery1.sql."""
from datetime import date, datetime, timezone
from decimal import Decimal

from sqlalchemy import Boolean, Date, DateTime, ForeignKey, Integer, Numeric, String, Text, UniqueConstraint, func
from sqlalchemy.orm import Mapped, mapped_column

from app.models.base import Base, CreatedAtMixin


class Supplier(Base):
    __tablename__ = "suppliers"

    supplier_id: Mapped[str] = mapped_column(String(50), primary_key=True)
    tax_code: Mapped[str] = mapped_column(String(50), unique=True, nullable=False)
    name: Mapped[str] = mapped_column(String(200), nullable=False)
    address: Mapped[str | None] = mapped_column(String(500))
    email: Mapped[str | None] = mapped_column(String(100))
    phone: Mapped[str | None] = mapped_column(String(20))
    is_active: Mapped[bool] = mapped_column(Boolean, default=True, nullable=False)


class ProductModel(Base, CreatedAtMixin):
    __tablename__ = "product_models"

    product_model_id: Mapped[str] = mapped_column(String(50), primary_key=True)
    product_name: Mapped[str] = mapped_column(String(200), nullable=False)
    category: Mapped[str] = mapped_column(String(100), nullable=False)
    unit: Mapped[str] = mapped_column(String(20), nullable=False)
    min_stock: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    is_active: Mapped[bool] = mapped_column(Boolean, default=True, nullable=False)


class SkuVariant(Base):
    __tablename__ = "sku_variants"
    __table_args__ = (UniqueConstraint("product_model_id", "color", "material", name="UQ_Variant"),)

    sku_id: Mapped[str] = mapped_column(String(100), primary_key=True)
    product_model_id: Mapped[str] = mapped_column(ForeignKey("product_models.product_model_id"), nullable=False)
    color: Mapped[str] = mapped_column(String(50), nullable=False)
    material: Mapped[str] = mapped_column(String(50), nullable=False)
    length_mm: Mapped[int] = mapped_column(Integer, nullable=False)
    width_mm: Mapped[int] = mapped_column(Integer, nullable=False)
    height_mm: Mapped[int] = mapped_column(Integer, nullable=False)
    cbm: Mapped[Decimal] = mapped_column(Numeric(18, 6), nullable=False)
    unit_price: Mapped[Decimal] = mapped_column(Numeric(18, 2), default=0, nullable=False)
    is_active: Mapped[bool] = mapped_column(Boolean, default=True, nullable=False)


class ComboProduct(Base):
    __tablename__ = "combo_products"

    combo_id: Mapped[str] = mapped_column(String(50), primary_key=True)
    combo_name: Mapped[str] = mapped_column(String(200), nullable=False)
    cbm_override: Mapped[Decimal | None] = mapped_column(Numeric(18, 6))
    min_stock: Mapped[int] = mapped_column(Integer, default=0, nullable=False)


class BomComponent(Base):
    __tablename__ = "bom_components"

    combo_id: Mapped[str] = mapped_column(ForeignKey("combo_products.combo_id"), primary_key=True)
    sku_id: Mapped[str] = mapped_column(ForeignKey("sku_variants.sku_id"), primary_key=True)
    quantity_per_set: Mapped[int] = mapped_column(Integer, nullable=False)


class PurchaseOrder(Base, CreatedAtMixin):
    __tablename__ = "purchase_orders"

    po_id: Mapped[str] = mapped_column(String(50), primary_key=True)
    po_date: Mapped[date] = mapped_column(Date, nullable=False)
    supplier_id: Mapped[str] = mapped_column(ForeignKey("suppliers.supplier_id"), nullable=False)
    warehouse_id: Mapped[str] = mapped_column(ForeignKey("warehouses.warehouse_id"), nullable=False)
    status: Mapped[str] = mapped_column(String(20), nullable=False)
    total_amount: Mapped[Decimal] = mapped_column(Numeric(18, 2), default=0, nullable=False)
    created_by: Mapped[str] = mapped_column(ForeignKey("users.user_id"), nullable=False)


class PurchaseOrderLine(Base):
    __tablename__ = "purchase_order_lines"

    po_id: Mapped[str] = mapped_column(ForeignKey("purchase_orders.po_id"), primary_key=True)
    sku_id: Mapped[str] = mapped_column(ForeignKey("sku_variants.sku_id"), primary_key=True)
    qty_document: Mapped[int] = mapped_column(Integer, nullable=False)
    qty_actual_good: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    qty_actual_damaged: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    unit_cost: Mapped[Decimal] = mapped_column(Numeric(18, 2), nullable=False)
    variance_reason: Mapped[str | None] = mapped_column(String(500))
    damaged_reason: Mapped[str | None] = mapped_column(String(500))


class SalesOrder(Base, CreatedAtMixin):
    __tablename__ = "sales_orders"

    so_id: Mapped[str] = mapped_column(String(50), primary_key=True)
    so_date: Mapped[date] = mapped_column(Date, nullable=False)
    warehouse_id: Mapped[str] = mapped_column(ForeignKey("warehouses.warehouse_id"), nullable=False)
    status: Mapped[str] = mapped_column(String(20), nullable=False)
    total_amount: Mapped[Decimal] = mapped_column(Numeric(18, 2), default=0, nullable=False)
    created_by: Mapped[str] = mapped_column(ForeignKey("users.user_id"), nullable=False)


class SalesOrderLine(Base):
    __tablename__ = "sales_order_lines"

    line_id: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=True)
    so_id: Mapped[str] = mapped_column(ForeignKey("sales_orders.so_id"), nullable=False)
    sku_id: Mapped[str | None] = mapped_column(ForeignKey("sku_variants.sku_id"))
    combo_id: Mapped[str | None] = mapped_column(ForeignKey("combo_products.combo_id"))
    qty: Mapped[int] = mapped_column(Integer, nullable=False)
    unit_price: Mapped[Decimal] = mapped_column(Numeric(18, 2), nullable=False)


class StockTransfer(Base, CreatedAtMixin):
    __tablename__ = "stock_transfers"

    transfer_id: Mapped[str] = mapped_column(String(50), primary_key=True)
    from_warehouse_id: Mapped[str] = mapped_column(ForeignKey("warehouses.warehouse_id"), nullable=False)
    to_warehouse_id: Mapped[str] = mapped_column(ForeignKey("warehouses.warehouse_id"), nullable=False)
    status: Mapped[str] = mapped_column(String(20), nullable=False)
    created_by: Mapped[str] = mapped_column(ForeignKey("users.user_id"), nullable=False)


class StockTransferLine(Base):
    __tablename__ = "stock_transfer_lines"

    transfer_id: Mapped[str] = mapped_column(ForeignKey("stock_transfers.transfer_id"), primary_key=True)
    sku_id: Mapped[str] = mapped_column(ForeignKey("sku_variants.sku_id"), primary_key=True)
    qty: Mapped[int] = mapped_column(Integer, nullable=False)


class StockAdjustment(Base):
    __tablename__ = "stock_adjustments"

    adjustment_id: Mapped[str] = mapped_column(String(50), primary_key=True)
    sku_id: Mapped[str] = mapped_column(ForeignKey("sku_variants.sku_id"), nullable=False)
    warehouse_id: Mapped[str] = mapped_column(ForeignKey("warehouses.warehouse_id"), nullable=False)
    qty_delta: Mapped[int] = mapped_column(Integer, nullable=False)
    reason: Mapped[str] = mapped_column(String(500), nullable=False)
    approved_by: Mapped[str] = mapped_column(ForeignKey("users.user_id"), nullable=False)
    created_at: Mapped[datetime] = mapped_column(DateTime, server_default=func.sysdatetime(), nullable=False)


class AiSuggestion(Base):
    __tablename__ = "ai_suggestions"

    suggestion_id: Mapped[str] = mapped_column(String(50), primary_key=True)
    suggestion_type: Mapped[str] = mapped_column(String(50), nullable=False)
    sku_id: Mapped[str | None] = mapped_column(ForeignKey("sku_variants.sku_id"))
    payload_json: Mapped[str] = mapped_column(Text, nullable=False)
    status: Mapped[str] = mapped_column(String(20), default="PENDING_REVIEW", nullable=False)
    reviewed_by: Mapped[str | None] = mapped_column(ForeignKey("users.user_id"))
    created_at: Mapped[datetime] = mapped_column(DateTime, server_default=func.sysdatetime(), nullable=False)
    reviewed_at: Mapped[datetime | None] = mapped_column(DateTime)
