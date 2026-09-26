"""Data Access Layer cho phiếu nhập kho."""
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.models.business import PurchaseOrder, PurchaseOrderLine, SkuVariant, Supplier
from app.models.core import Warehouse


class InboundRepository:
    def __init__(self, session: AsyncSession) -> None:
        self._session = session

    async def get_order(self, po_id: str) -> PurchaseOrder | None:
        return await self._session.get(PurchaseOrder, po_id)

    async def lines(self, po_id: str) -> list[PurchaseOrderLine]:
        statement = select(PurchaseOrderLine).where(PurchaseOrderLine.po_id == po_id).order_by(PurchaseOrderLine.sku_id)
        return list((await self._session.scalars(statement)).all())

    async def active_supplier(self, supplier_id: str) -> Supplier | None:
        supplier = await self._session.get(Supplier, supplier_id)
        return supplier if supplier and supplier.is_active else None

    async def active_warehouse(self, warehouse_id: str) -> Warehouse | None:
        warehouse = await self._session.get(Warehouse, warehouse_id)
        return warehouse if warehouse and warehouse.is_active else None

    async def active_sku(self, sku_id: str) -> SkuVariant | None:
        sku = await self._session.get(SkuVariant, sku_id)
        return sku if sku and sku.is_active else None

    def add(self, entity: object) -> None:
        self._session.add(entity)
