"""Repository for stock transfers."""
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.models.business import StockTransfer, StockTransferLine


class TransferRepository:
	def __init__(self, session: AsyncSession) -> None:
		self._session = session

	async def get(self, transfer_id: str) -> StockTransfer | None:
		return await self._session.get(StockTransfer, transfer_id)

	async def lines(self, transfer_id: str) -> list[StockTransferLine]:
		statement = select(StockTransferLine).where(StockTransferLine.transfer_id == transfer_id)
		return list((await self._session.scalars(statement)).all())

	def add(self, entity: object) -> None:
		self._session.add(entity)

