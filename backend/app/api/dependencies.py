"""Dependencies xác thực và RBAC dùng cho mọi endpoint bảo vệ."""
from collections.abc import Callable

from fastapi import Depends
from fastapi.security import HTTPAuthorizationCredentials, HTTPBearer
from sqlalchemy.ext.asyncio import AsyncSession

from app.core.database import get_db_session
from app.core.exceptions import forbidden, unauthorized
from app.core.security import decode_access_token
from app.models.core import User
from app.services.auth_service import AuthService

bearer_scheme = HTTPBearer(auto_error=False)


async def get_current_user(
    credentials: HTTPAuthorizationCredentials | None = Depends(bearer_scheme),
    session: AsyncSession = Depends(get_db_session),
) -> User:
    if credentials is None:
        raise unauthorized("Thiếu Bearer access token.")
    try:
        payload = decode_access_token(credentials.credentials)
    except ValueError as exc:
        raise unauthorized(str(exc)) from exc
    return await AuthService(session).get_active_user(payload["sub"])


def require_roles(*allowed_roles: str) -> Callable:
    """Tạo dependency kiểm tra quyền trước khi controller xử lý request."""
    async def guard(current_user: User = Depends(get_current_user)) -> User:
        if current_user.role_id not in allowed_roles:
            raise forbidden()
        return current_user

    return guard
