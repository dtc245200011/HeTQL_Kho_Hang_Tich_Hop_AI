"""Repository for AI suggestions (proposals)."""
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.models.business import AiSuggestion


class AiRepository:
	def __init__(self, session: AsyncSession) -> None:
		self._session = session

	async def get(self, suggestion_id: str) -> AiSuggestion | None:
		return await self._session.get(AiSuggestion, suggestion_id)

	async def list(self, status: str | None = None) -> list[AiSuggestion]:
		statement = select(AiSuggestion)
		if status:
			statement = statement.where(AiSuggestion.status == status)
		return list((await self._session.scalars(statement)).all())

	def add(self, entity: object) -> None:
		self._session.add(entity)

	async def update_status(self, suggestion_id: str, status: str, reviewed_by: str | None = None, reviewed_at=None) -> AiSuggestion | None:
		item = await self.get(suggestion_id)
		if not item:
			return None
		item.status = status
		if reviewed_by:
			item.reviewed_by = reviewed_by
		if reviewed_at:
			item.reviewed_at = reviewed_at
		return item
