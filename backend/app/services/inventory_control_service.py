"""Application service Inventory Control."""
from sqlalchemy.ext.asyncio import AsyncSession

from app.repositories.inventory_repository import InventoryRepository
from app.schemas.inventory import DamageStockRequest
from app.services.inventory_service import InventoryService


class InventoryControlService:
    def __init__(self, session: AsyncSession, actor_id: str) -> None:
        self._session, self._actor_id = session, actor_id
        self._repository = InventoryRepository(session)

    async def search_stock(self, warehouse_id: str | None, sku_id: str | None):
        return await self._repository.search(warehouse_id, sku_id)

    async def mark_damaged(self, payload: DamageStockRequest) -> None:
        # Dependency xác thực có thể đã mở read transaction.
        if self._session.in_transaction():
            await self._session.commit()
        try:
            async with self._session.begin():
                await InventoryService(self._session).mark_damaged(
                    sku_id=payload.sku_id, warehouse_id=payload.warehouse_id,
                    quantity=payload.quantity, reason=payload.reason, actor_id=self._actor_id,
                )
        except Exception:
            await self._session.rollback()
            raise
