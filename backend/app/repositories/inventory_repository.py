"""DAL chỉ đọc dữ liệu tồn; mọi thay đổi đi qua InventoryService."""
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.models.core import StockLedger


class InventoryRepository:
    def __init__(self, session: AsyncSession) -> None:
        self._session = session

    async def search(self, warehouse_id: str | None, sku_id: str | None) -> list[StockLedger]:
        statement = select(StockLedger).order_by(StockLedger.warehouse_id, StockLedger.sku_id, StockLedger.status)
        if warehouse_id:
            statement = statement.where(StockLedger.warehouse_id == warehouse_id)
        if sku_id:
            statement = statement.where(StockLedger.sku_id == sku_id)
        return list((await self._session.scalars(statement)).all())
