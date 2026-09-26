"""Controller REST cho luồng nhập kho."""
from fastapi import APIRouter, Depends, status
from sqlalchemy.ext.asyncio import AsyncSession

from app.api.dependencies import get_current_user, require_roles
from app.core.database import get_db_session
from app.domain.enums import RoleId
from app.models.core import User
from app.schemas.inbound import PurchaseOrderCreate, PurchaseOrderResponse, QcSubmission
from app.services.inbound_service import InboundService

router = APIRouter(prefix="/inbound/purchase-orders", tags=["Inbound"])
CREATE_ROLES = (RoleId.ADMIN.value, RoleId.WAREHOUSE_KEEPER.value, RoleId.STAFF.value)
QC_ROLES = (RoleId.ADMIN.value, RoleId.QC.value, RoleId.WAREHOUSE_KEEPER.value)
APPROVAL_ROLES = (RoleId.ACCOUNTANT.value, RoleId.WAREHOUSE_MANAGER.value)


def service_for(session: AsyncSession = Depends(get_db_session), current_user: User = Depends(get_current_user)) -> InboundService:
    return InboundService(session, current_user.user_id, current_user.role_id)


@router.post("", response_model=PurchaseOrderResponse, status_code=status.HTTP_201_CREATED)
async def create_order(payload: PurchaseOrderCreate, service: InboundService = Depends(service_for), _: User = Depends(require_roles(*CREATE_ROLES))):
    return await service.create_order(payload)


@router.get("/{po_id}", response_model=PurchaseOrderResponse)
async def get_order(po_id: str, service: InboundService = Depends(service_for)):
    return await service.get_order(po_id)


@router.post("/{po_id}/qc", response_model=PurchaseOrderResponse)
async def submit_qc(po_id: str, payload: QcSubmission, service: InboundService = Depends(service_for), _: User = Depends(require_roles(*QC_ROLES))):
    return await service.submit_qc(po_id, payload)


@router.post("/{po_id}/approve", response_model=PurchaseOrderResponse)
async def approve_order(po_id: str, service: InboundService = Depends(service_for), _: User = Depends(require_roles(*APPROVAL_ROLES))):
    return await service.approve_order(po_id)
