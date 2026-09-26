"""Endpoint tồn kho; API ghi tồn chỉ dành cho Quản lý kho/Admin."""
from fastapi import APIRouter, Depends, status
from sqlalchemy.ext.asyncio import AsyncSession

from app.api.dependencies import get_current_user, require_roles
from app.core.database import get_db_session
from app.domain.enums import RoleId
from app.models.core import User
from app.schemas.inventory import DamageStockRequest, StockResponse
from app.services.inventory_control_service import InventoryControlService

router = APIRouter(prefix="/inventory", tags=["Inventory Control"])


def service_for(session: AsyncSession = Depends(get_db_session), user: User = Depends(get_current_user)) -> InventoryControlService:
    return InventoryControlService(session, user.user_id)


@router.get("", response_model=list[StockResponse])
async def search_stock(warehouse_id: str | None = None, sku_id: str | None = None, service: InventoryControlService = Depends(service_for)):
    return await service.search_stock(warehouse_id, sku_id)


@router.post("/damaged", status_code=status.HTTP_204_NO_CONTENT)
async def mark_damaged(payload: DamageStockRequest, service: InventoryControlService = Depends(service_for), _: User = Depends(require_roles(RoleId.ADMIN.value, RoleId.WAREHOUSE_MANAGER.value))):
    await service.mark_damaged(payload)
