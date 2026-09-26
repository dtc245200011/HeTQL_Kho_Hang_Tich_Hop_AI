"""Chuyển kho, điều chỉnh, báo cáo và AI suggestions."""
import json
from datetime import date, datetime, time, timezone
from uuid import uuid4

from fastapi import APIRouter, Depends, status
from sqlalchemy import func, select
from sqlalchemy.ext.asyncio import AsyncSession

from app.api.dependencies import get_current_user, require_roles
from app.core.database import get_db_session
from app.core.exceptions import BusinessError
from app.domain.enums import DocumentStatus, RoleId
from app.models.business import AiSuggestion, ProductModel, PurchaseOrder, PurchaseOrderLine, SalesOrder, SalesOrderLine, SkuVariant, StockAdjustment, StockTransfer, StockTransferLine
from app.models.core import StockLedger, StockMovement, User, Warehouse
from app.schemas.operations import AdjustmentCreate, AiSuggestionResponse, NxtReportLine, TransferCreate
from app.services.ai_service import AiService
from app.services.audit_service import AuditService
from app.services.inventory_service import InventoryService
from app.services.reporting_service import compute_inventory_summary_from_movements
from app.services.stock_transfer_service import StockTransferService
from app.services.stock_adjustment_service import StockAdjustmentService

router = APIRouter(tags=["Operations, Reports & AI"])

async def current(session: AsyncSession = Depends(get_db_session), user: User = Depends(get_current_user)):
    return session, user

@router.post('/transfers', status_code=status.HTTP_201_CREATED)
async def create_transfer(payload: TransferCreate, data=Depends(current), _: User = Depends(require_roles(RoleId.ADMIN.value, RoleId.WAREHOUSE_KEEPER.value, RoleId.WAREHOUSE_MANAGER.value))):
    session, user = data
    service = StockTransferService(session, user.user_id, user.role_id)
    transfer = await service.create_transfer(payload)
    return {'transfer_id': transfer.transfer_id, 'status': transfer.status}

@router.post('/transfers/{transfer_id}/approve')
async def approve_transfer(transfer_id: str, data=Depends(current), _: User = Depends(require_roles(RoleId.ADMIN.value, RoleId.WAREHOUSE_MANAGER.value))):
    session, user = data
    service = StockTransferService(session, user.user_id, user.role_id)
    transfer = await service.approve_transfer(transfer_id)
    return {'transfer_id': transfer.transfer_id, 'status': transfer.status}

@router.post('/inventory/adjustments', status_code=status.HTTP_201_CREATED)
async def adjust_stock(payload: AdjustmentCreate, data=Depends(current), _: User = Depends(require_roles(RoleId.ADMIN.value, RoleId.WAREHOUSE_KEEPER.value, RoleId.WAREHOUSE_MANAGER.value))):
    session, user = data
    service = StockAdjustmentService(session, user.user_id, user.role_id)
    adj = await service.create_adjustment(payload)
    return {'adjustment_id': adj.adjustment_id}

@router.get('/reports/inventory-summary', response_model=list[NxtReportLine])
async def inventory_summary(date_from: date, date_to: date, warehouse_id: str | None = None, _: User = Depends(get_current_user), session: AsyncSession = Depends(get_db_session)):
    if date_from > date_to: raise BusinessError(422, 'ERR-REPORT-DATE', 'date_from phải nhỏ hơn hoặc bằng date_to.')
    start, end = datetime.combine(date_from, time.min), datetime.combine(date_to, time.max)
    # Query raw movements and compute summary in Python to allow reliable unit testing of logic
    stmt = select(StockMovement).where(StockMovement.stock_status == 'GOOD', StockMovement.performed_at <= end)
    if warehouse_id:
        stmt = stmt.where(StockMovement.warehouse_id == warehouse_id)
    rows = list((await session.scalars(stmt.order_by(StockMovement.sku_id, StockMovement.warehouse_id, StockMovement.performed_at))).all())
    movements = [
        {"sku_id": r.sku_id, "warehouse_id": r.warehouse_id, "quantity_delta": r.quantity_delta, "performed_at": r.performed_at}
        for r in rows
    ]
    summary = compute_inventory_summary_from_movements(movements, start, end)
    return [NxtReportLine(**item) for item in summary]


@router.get('/reports/adjustments', response_model=list[dict])
async def list_adjustments(date_from: date | None = None, date_to: date | None = None, warehouse_id: str | None = None, sku_id: str | None = None, _: User = Depends(require_roles(RoleId.ADMIN.value, RoleId.ACCOUNTANT.value, RoleId.WAREHOUSE_MANAGER.value)), session: AsyncSession = Depends(get_db_session)):
    statement = select(StockAdjustment)
    if date_from:
        statement = statement.where(StockAdjustment.created_at >= datetime.combine(date_from, time.min))
    if date_to:
        statement = statement.where(StockAdjustment.created_at <= datetime.combine(date_to, time.max))
    if warehouse_id:
        statement = statement.where(StockAdjustment.warehouse_id == warehouse_id)
    if sku_id:
        statement = statement.where(StockAdjustment.sku_id == sku_id)
    rows = list((await session.scalars(statement.order_by(StockAdjustment.created_at.desc()))).all())
    # convert to simple dicts for response
    result = []
    for item in rows:
        result.append({
            'adjustment_id': item.adjustment_id,
            'sku_id': item.sku_id,
            'warehouse_id': item.warehouse_id,
            'qty_delta': item.qty_delta,
            'reason': item.reason,
            'approved_by': item.approved_by,
            'created_at': item.created_at,
        })
    return result


@router.get('/reports/transfers', response_model=list[dict])
async def list_transfers(date_from: date | None = None, date_to: date | None = None, warehouse_id: str | None = None, sku_id: str | None = None, status: str | None = None, _: User = Depends(require_roles(RoleId.ADMIN.value, RoleId.ACCOUNTANT.value, RoleId.WAREHOUSE_MANAGER.value)), session: AsyncSession = Depends(get_db_session)):
    statement = select(StockTransfer)
    if date_from:
        statement = statement.where(StockTransfer.created_at >= datetime.combine(date_from, time.min))
    if date_to:
        statement = statement.where(StockTransfer.created_at <= datetime.combine(date_to, time.max))
    if warehouse_id:
        statement = statement.where((StockTransfer.from_warehouse_id == warehouse_id) | (StockTransfer.to_warehouse_id == warehouse_id))
    if status:
        statement = statement.where(StockTransfer.status == status)
    transfers = list((await session.scalars(statement.order_by(StockTransfer.created_at.desc()))).all())
    result = []
    for t in transfers:
        lines = list((await session.scalars(select(StockTransferLine).where(StockTransferLine.transfer_id == t.transfer_id))).all())
        if sku_id and not any(l.sku_id == sku_id for l in lines):
            continue
        result.append({
            'transfer_id': t.transfer_id,
            'from_warehouse_id': t.from_warehouse_id,
            'to_warehouse_id': t.to_warehouse_id,
            'status': t.status,
            'created_by': t.created_by,
            'lines': [{'sku_id': l.sku_id, 'qty': l.qty} for l in lines],
            'created_at': getattr(t, 'created_at', None),
        })
    return result

@router.post('/ai/suggestions/replenishment', response_model=list[AiSuggestionResponse])
async def generate_replenishment(data=Depends(current), _: User = Depends(require_roles(RoleId.ADMIN.value, RoleId.WAREHOUSE_MANAGER.value))):
    session, user = data
    service = AiService(session, user.user_id, user.role_id)
    items = await service.generate_replenishment()
    return [AiSuggestionResponse.model_validate(item) for item in items]

@router.get('/ai/suggestions', response_model=list[AiSuggestionResponse])
async def list_suggestions(_: User = Depends(require_roles(RoleId.ADMIN.value, RoleId.WAREHOUSE_MANAGER.value)), session: AsyncSession = Depends(get_db_session), user: User = Depends(get_current_user)):
    service = AiService(session, user.user_id, user.role_id)
    items = await service.list_pending()
    return [AiSuggestionResponse.model_validate(item) for item in items]

@router.post('/ai/suggestions/{suggestion_id}/approve')
async def approve_suggestion(suggestion_id: str, data=Depends(current), _: User = Depends(require_roles(RoleId.ADMIN.value, RoleId.WAREHOUSE_MANAGER.value))):
    session, user = data
    service = AiService(session, user.user_id, user.role_id)
    item = await service.approve(suggestion_id)
    return {'suggestion_id': item.suggestion_id, 'status': item.status}


@router.post('/ai/suggestions/{suggestion_id}/reject')
async def reject_suggestion(suggestion_id: str, reason: str | None = None, data=Depends(current), _: User = Depends(require_roles(RoleId.ADMIN.value, RoleId.WAREHOUSE_MANAGER.value))):
    session, user = data
    service = AiService(session, user.user_id, user.role_id)
    item = await service.reject(suggestion_id, reason)
    return {'suggestion_id': item.suggestion_id, 'status': item.status}
