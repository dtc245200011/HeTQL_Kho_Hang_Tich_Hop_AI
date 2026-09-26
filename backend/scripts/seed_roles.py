"""Khởi tạo vai trò chuẩn. Chạy: python scripts/seed_roles.py."""
import asyncio

from sqlalchemy import select

from app.core.database import AsyncSessionFactory
from app.domain.enums import RoleId
from app.models.core import Role

ROLE_NAMES = {
    RoleId.ADMIN: "Quản trị hệ thống",
    RoleId.WAREHOUSE_KEEPER: "Thủ kho",
    RoleId.ACCOUNTANT: "Kế toán",
    RoleId.WAREHOUSE_MANAGER: "Quản lý kho",
    RoleId.DIRECTOR: "Giám đốc",
    RoleId.QC: "Nhân viên QC",
    RoleId.STAFF: "Nhân viên",
}


async def seed_roles() -> None:
    async with AsyncSessionFactory.begin() as session:
        existing = set((await session.scalars(select(Role.role_id))).all())
        for role_id, role_name in ROLE_NAMES.items():
            if role_id not in existing:
                session.add(Role(role_id=role_id.value, role_name=role_name))


if __name__ == "__main__":
    asyncio.run(seed_roles())
