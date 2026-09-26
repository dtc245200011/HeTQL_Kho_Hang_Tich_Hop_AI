"""Nghiệp vụ xác thực tách biệt controller và repository."""
from sqlalchemy.ext.asyncio import AsyncSession

from app.core.exceptions import unauthorized
from app.core.security import create_access_token, verify_password
from app.models.core import User
from app.repositories.user_repository import UserRepository


class AuthService:
    def __init__(self, session: AsyncSession) -> None:
        self._users = UserRepository(session)

    async def authenticate(self, username: str, password: str) -> str:
        user = await self._users.get_by_username(username)
        if user is None or not user.is_active or not verify_password(password, user.password_hash):
            raise unauthorized("Tên đăng nhập hoặc mật khẩu không đúng.")
        return create_access_token(subject=user.user_id, role_id=user.role_id)

    async def get_active_user(self, user_id: str) -> User:
        user = await self._users.get_by_id(user_id)
        if user is None or not user.is_active:
            raise unauthorized("Tài khoản không tồn tại hoặc đã ngừng hoạt động.")
        return user
