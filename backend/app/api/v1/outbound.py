"""REST controller cho xuất kho."""
from fastapi import APIRouter, Depends, status
from sqlalchemy.ext.asyncio import AsyncSession

from app.api.dependencies import get_current_user, require_roles
from app.core.database import get_db_session
from app.domain.enums import RoleId
from app.models.core import User
from app.schemas.outbound import SalesOrderCreate, SalesOrderResponse
from app.services.outbound_service import OutboundService

router = APIRouter(prefix="/outbound/sales-orders", tags=["Outbound"])


def service_for(session: AsyncSession = Depends(get_db_session), user: User = Depends(get_current_user)) -> OutboundService:
    return OutboundService(session, user.user_id, user.role_id)


@router.post("", response_model=SalesOrderResponse, status_code=status.HTTP_201_CREATED)
async def create_order(payload: SalesOrderCreate, service: OutboundService = Depends(service_for), _: User = Depends(require_roles(RoleId.ADMIN.value, RoleId.WAREHOUSE_KEEPER.value, RoleId.STAFF.value))):
    return await service.create_order(payload)


@router.get("/{so_id}", response_model=SalesOrderResponse)
async def get_order(so_id: str, service: OutboundService = Depends(service_for)):
    return await service.get_order(so_id)


@router.post("/{so_id}/approve", response_model=SalesOrderResponse)
async def approve_order(so_id: str, service: OutboundService = Depends(service_for), _: User = Depends(require_roles(RoleId.ACCOUNTANT.value, RoleId.WAREHOUSE_MANAGER.value, RoleId.DIRECTOR.value))):
    return await service.approve_order(so_id)
