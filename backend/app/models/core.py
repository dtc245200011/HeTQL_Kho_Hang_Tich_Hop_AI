"""ORM nền tảng: RBAC, kho, stock ledger và audit log.

Các bảng khớp trực tiếp với SQLQuery1.sql. StockLedger chỉ được thay đổi từ
InventoryService; repository không cung cấp API update số lượng công khai.
"""
from datetime import datetime, timezone
from decimal import Decimal

from sqlalchemy import BIGINT, Boolean, DateTime, ForeignKey, Integer, Numeric, String, Text, func
from sqlalchemy.orm import Mapped, mapped_column, relationship

from app.models.base import Base, CreatedAtMixin


class Role(Base):
    __tablename__ = "roles"

    role_id: Mapped[str] = mapped_column(String(50), primary_key=True)
    role_name: Mapped[str] = mapped_column(String(100), nullable=False)
    users: Mapped[list["User"]] = relationship(back_populates="role")


class User(Base, CreatedAtMixin):
    __tablename__ = "users"

    user_id: Mapped[str] = mapped_column(String(50), primary_key=True)
    username: Mapped[str] = mapped_column(String(50), unique=True, nullable=False, index=True)
    password_hash: Mapped[str] = mapped_column(String(255), nullable=False)
    full_name: Mapped[str] = mapped_column(String(100), nullable=False)
    role_id: Mapped[str] = mapped_column(ForeignKey("roles.role_id"), nullable=False, index=True)
    is_active: Mapped[bool] = mapped_column(Boolean, default=True, nullable=False)
    role: Mapped[Role] = relationship(back_populates="users")


class Warehouse(Base):
    __tablename__ = "warehouses"

    warehouse_id: Mapped[str] = mapped_column(String(50), primary_key=True)
    warehouse_name: Mapped[str] = mapped_column(String(100), nullable=False)
    warehouse_type: Mapped[str] = mapped_column(String(50), nullable=False)
    max_capacity_cbm: Mapped[Decimal] = mapped_column(Numeric(18, 4), nullable=False)
    is_active: Mapped[bool] = mapped_column(Boolean, default=True, nullable=False)


class StockLedger(Base):
    __tablename__ = "stock_ledger"

    sku_id: Mapped[str] = mapped_column(String(100), ForeignKey("sku_variants.sku_id"), primary_key=True)
    warehouse_id: Mapped[str] = mapped_column(String(50), ForeignKey("warehouses.warehouse_id"), primary_key=True)
    status: Mapped[str] = mapped_column(String(20), primary_key=True)
    quantity: Mapped[int] = mapped_column(Integer, nullable=False, default=0)
    updated_at: Mapped[datetime] = mapped_column(DateTime, server_default=func.sysdatetime(), onupdate=func.sysdatetime(), nullable=False)


class StockMovement(Base):
    __tablename__ = "stock_movements"
    movement_id: Mapped[int] = mapped_column(BIGINT, primary_key=True, autoincrement=True)
    sku_id: Mapped[str] = mapped_column(String(100), ForeignKey("sku_variants.sku_id"), nullable=False)
    warehouse_id: Mapped[str] = mapped_column(String(50), ForeignKey("warehouses.warehouse_id"), nullable=False)
    stock_status: Mapped[str] = mapped_column(String(20), nullable=False)
    quantity_delta: Mapped[int] = mapped_column(Integer, nullable=False)
    movement_type: Mapped[str] = mapped_column(String(30), nullable=False)
    reference_type: Mapped[str] = mapped_column(String(50), nullable=False)
    reference_id: Mapped[str] = mapped_column(String(100), nullable=False)
    performed_by: Mapped[str] = mapped_column(ForeignKey("users.user_id"), nullable=False)
    performed_at: Mapped[datetime] = mapped_column(DateTime, server_default=func.sysdatetime(), nullable=False)


class AuditLog(Base):
    __tablename__ = "audit_logs"

    log_id: Mapped[int] = mapped_column(BIGINT, primary_key=True, autoincrement=True)
    entity_type: Mapped[str] = mapped_column(String(50), nullable=False)
    entity_id: Mapped[str] = mapped_column(String(100), nullable=False)
    action: Mapped[str] = mapped_column(String(20), nullable=False)
    before_json: Mapped[str | None] = mapped_column(Text)
    after_json: Mapped[str | None] = mapped_column(Text)
    performed_by: Mapped[str] = mapped_column(ForeignKey("users.user_id"), nullable=False)
    performed_at: Mapped[datetime] = mapped_column(DateTime, server_default=func.sysdatetime(), nullable=False)
    reason: Mapped[str | None] = mapped_column(String(500))
