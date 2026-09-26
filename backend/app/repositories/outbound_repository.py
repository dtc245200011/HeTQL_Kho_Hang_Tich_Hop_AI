"""DAL cho phiếu xuất và BOM dùng khi duyệt xuất."""
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.models.business import BomComponent, ComboProduct, SalesOrder, SalesOrderLine, SkuVariant
from app.models.core import Warehouse


class OutboundRepository:
    def __init__(self, session: AsyncSession) -> None:
        self._session = session

    async def order(self, so_id: str) -> SalesOrder | None:
        return await self._session.get(SalesOrder, so_id)

    async def lines(self, so_id: str) -> list[SalesOrderLine]:
        return list((await self._session.scalars(select(SalesOrderLine).where(SalesOrderLine.so_id == so_id).order_by(SalesOrderLine.line_id))).all())

    async def active_warehouse(self, warehouse_id: str) -> Warehouse | None:
        value = await self._session.get(Warehouse, warehouse_id)
        return value if value and value.is_active else None

    async def active_sku(self, sku_id: str) -> SkuVariant | None:
        value = await self._session.get(SkuVariant, sku_id)
        return value if value and value.is_active else None

    async def combo_components(self, combo_id: str) -> list[BomComponent] | None:
        combo = await self._session.get(ComboProduct, combo_id)
        if combo is None:
            return None
        return list((await self._session.scalars(select(BomComponent).where(BomComponent.combo_id == combo_id))).all())

    def add(self, entity: object) -> None:
        self._session.add(entity)
