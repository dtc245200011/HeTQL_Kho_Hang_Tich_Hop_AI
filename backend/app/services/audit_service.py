"""Ghi audit log cho mọi thao tác CUD/Approve theo SRS-F-22."""
import json
from typing import Any

from sqlalchemy.ext.asyncio import AsyncSession

from app.models.core import AuditLog


class AuditService:
    def __init__(self, session: AsyncSession) -> None:
        self._session = session

    def record(
        self, *, entity_type: str, entity_id: str, action: str, performed_by: str,
        before: dict[str, Any] | None = None, after: dict[str, Any] | None = None,
        reason: str | None = None,
    ) -> None:
        self._session.add(AuditLog(
            entity_type=entity_type, entity_id=entity_id, action=action,
            before_json=json.dumps(before, ensure_ascii=False, default=str) if before else None,
            after_json=json.dumps(after, ensure_ascii=False, default=str) if after else None,
            performed_by=performed_by, reason=reason,
        ))
