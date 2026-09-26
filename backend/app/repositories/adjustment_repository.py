"""Repository for stock adjustments."""
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.models.business import StockAdjustment


class AdjustmentRepository:
	def __init__(self, session: AsyncSession) -> None:
		self._session = session

	async def get(self, adjustment_id: str) -> StockAdjustment | None:
		return await self._session.get(StockAdjustment, adjustment_id)

	def add(self, entity: object) -> None:
		self._session.add(entity)


