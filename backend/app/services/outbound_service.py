"""Nghiệp vụ xuất kho, duyệt đa cấp và phân rã Combo/BOM."""
from decimal import Decimal

from sqlalchemy.ext.asyncio import AsyncSession

from app.core.exceptions import BusinessError
from app.domain.enums import DocumentStatus, RoleId
from app.models.business import SalesOrder, SalesOrderLine
from app.repositories.outbound_repository import OutboundRepository
from app.schemas.outbound import SalesOrderCreate, SalesOrderResponse
from app.services.audit_service import AuditService
from app.services.inventory_service import InventoryService


class OutboundService:
    def __init__(self, session: AsyncSession, actor_id: str, actor_role: str) -> None:
        self._session, self._actor_id, self._actor_role = session, actor_id, actor_role
        self._repo, self._audit = OutboundRepository(session), AuditService(session)

    async def _start_write(self) -> None:
        if self._session.in_transaction():
            await self._session.commit()

    @staticmethod
    def _response(order: SalesOrder, lines: list[SalesOrderLine]) -> SalesOrderResponse:
        return SalesOrderResponse(so_id=order.so_id, so_date=order.so_date, warehouse_id=order.warehouse_id, status=order.status, total_amount=order.total_amount, created_by=order.created_by, lines=lines)

    async def get_order(self, so_id: str) -> SalesOrderResponse:
        order = await self._repo.order(so_id)
        if not order:
            raise BusinessError(404, "ERR-SO-404", "Không tìm thấy phiếu xuất.")
        return self._response(order, await self._repo.lines(so_id))

    async def create_order(self, payload: SalesOrderCreate) -> SalesOrderResponse:
        await self._start_write()
        async with self._session.begin():
            if await self._repo.order(payload.so_id):
                raise BusinessError(409, "ERR-SO-DUPLICATE", "Số phiếu xuất đã tồn tại.")
            if await self._repo.active_warehouse(payload.warehouse_id) is None:
                raise BusinessError(422, "ERR-WAREHOUSE-INACTIVE", "Kho xuất không tồn tại hoặc đã ngừng hoạt động.")
            for line in payload.lines:
                if line.sku_id and await self._repo.active_sku(line.sku_id) is None:
                    raise BusinessError(422, "ERR-SKU-INACTIVE", f"SKU {line.sku_id} không tồn tại hoặc ngừng kinh doanh.")
                if line.combo_id and not await self._repo.combo_components(line.combo_id):
                    raise BusinessError(422, "ERR-COMBO-INVALID", f"Combo {line.combo_id} không tồn tại hoặc không có BOM.")
            total = sum((line.qty * line.unit_price for line in payload.lines), Decimal("0"))
            order = SalesOrder(so_id=payload.so_id, so_date=payload.so_date, warehouse_id=payload.warehouse_id, status=DocumentStatus.DRAFT.value, total_amount=total, created_by=self._actor_id)
            self._repo.add(order)
            lines = [SalesOrderLine(so_id=order.so_id, **line.model_dump()) for line in payload.lines]
            for line in lines:
                self._repo.add(line)
            await self._session.flush()
            self._audit.record(entity_type="SALES_ORDER", entity_id=order.so_id, action="CREATE", performed_by=self._actor_id, after={"status": order.status, "total_amount": total})
        return self._response(order, lines)

    async def _requirements(self, lines: list[SalesOrderLine]) -> dict[str, int]:
        required: dict[str, int] = {}
        for line in lines:
            if line.sku_id:
                required[line.sku_id] = required.get(line.sku_id, 0) + line.qty
            else:
                components = await self._repo.combo_components(line.combo_id or "")
                if not components:
                    raise BusinessError(422, "ERR-COMBO-INVALID", "Combo không tồn tại hoặc BOM rỗng.")
                for component in components:
                    required[component.sku_id] = required.get(component.sku_id, 0) + component.quantity_per_set * line.qty
        return required

    async def approve_order(self, so_id: str) -> SalesOrderResponse:
        await self._start_write()
        async with self._session.begin():
            order = await self._repo.order(so_id)
            if not order:
                raise BusinessError(404, "ERR-SO-404", "Không tìm thấy phiếu xuất.")
            lines, before = await self._repo.lines(so_id), order.status
            final_approval = False
            if order.status == DocumentStatus.DRAFT.value and self._actor_role == RoleId.ACCOUNTANT.value:
                order.status = DocumentStatus.PENDING_L2.value
            elif order.status == DocumentStatus.PENDING_L2.value and self._actor_role == RoleId.WAREHOUSE_MANAGER.value:
                if order.total_amount > Decimal("100000000"):
                    order.status = DocumentStatus.PENDING_L3.value
                else:
                    order.status, final_approval = DocumentStatus.APPROVED.value, True
            elif order.status == DocumentStatus.PENDING_L3.value and self._actor_role == RoleId.DIRECTOR.value:
                order.status, final_approval = DocumentStatus.APPROVED.value, True
            else:
                raise BusinessError(403, "ERR-APPROVAL-001", "Bạn không có quyền duyệt ở bước này hoặc phiếu sai trạng thái.")
            if final_approval:
                await InventoryService(self._session).apply_outbound(warehouse_id=order.warehouse_id, requirements=await self._requirements(lines), actor_id=self._actor_id, reference_id=so_id)
            self._audit.record(entity_type="SALES_ORDER", entity_id=so_id, action="APPROVE", performed_by=self._actor_id, before={"status": before}, after={"status": order.status}, reason="Duyệt phiếu xuất" if not final_approval else "Duyệt cuối và trừ tồn")
        return self._response(order, lines)
