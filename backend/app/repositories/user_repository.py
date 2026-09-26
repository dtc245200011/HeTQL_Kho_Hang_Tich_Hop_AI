"""Truy vấn user, không chứa chính sách đăng nhập."""
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.models.core import User


class UserRepository:
    def __init__(self, session: AsyncSession) -> None:
        self._session = session

    async def get_by_username(self, username: str) -> User | None:
        return await self._session.scalar(select(User).where(User.username == username))

    async def get_by_id(self, user_id: str) -> User | None:
        return await self._session.get(User, user_id)
