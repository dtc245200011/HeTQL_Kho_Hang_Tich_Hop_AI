"""Nghiệp vụ nhập kho, QC và duyệt hai cấp."""
from decimal import Decimal

from sqlalchemy.ext.asyncio import AsyncSession

from app.core.exceptions import BusinessError
from app.domain.enums import DocumentStatus, RoleId
from app.models.business import PurchaseOrder, PurchaseOrderLine
from app.repositories.inbound_repository import InboundRepository
from app.schemas.inbound import PurchaseOrderCreate, PurchaseOrderResponse, QcSubmission
from app.services.audit_service import AuditService
from app.services.inventory_service import InventoryService


class InboundService:
    def __init__(self, session: AsyncSession, actor_id: str, actor_role: str) -> None:
        self._session = session
        self._repo = InboundRepository(session)
        self._audit = AuditService(session)
        self._actor_id = actor_id
        self._actor_role = actor_role

    async def _start_write(self) -> None:
        """Đóng read transaction do dependency xác thực mở trước khi bắt đầu write transaction."""
        if self._session.in_transaction():
            await self._session.commit()

    @staticmethod
    def _response(order: PurchaseOrder, lines: list[PurchaseOrderLine]) -> PurchaseOrderResponse:
        return PurchaseOrderResponse(
            po_id=order.po_id, po_date=order.po_date, supplier_id=order.supplier_id,
            warehouse_id=order.warehouse_id, status=order.status, total_amount=order.total_amount,
            created_by=order.created_by, lines=lines,
        )

    @staticmethod
    def _ensure_qc_complete(lines: list[PurchaseOrderLine]) -> None:
        """Không cho duyệt nếu QC chưa ghi nhận từng dòng hoặc thiếu lý do lệch."""
        for line in lines:
            actual = line.qty_actual_good + line.qty_actual_damaged
            if actual != line.qty_document and not line.variance_reason:
                raise BusinessError(422, "ERR-QC-INCOMPLETE", "Phiếu chưa hoàn tất QC hoặc chưa nêu lý do chênh lệch.")

    async def get_order(self, po_id: str) -> PurchaseOrderResponse:
        order = await self._repo.get_order(po_id)
        if order is None:
            raise BusinessError(404, "ERR-PO-404", "Không tìm thấy phiếu nhập.")
        return self._response(order, await self._repo.lines(po_id))

    async def create_order(self, payload: PurchaseOrderCreate) -> PurchaseOrderResponse:
        await self._start_write()
        async with self._session.begin():
            if await self._repo.get_order(payload.po_id):
                raise BusinessError(409, "ERR-PO-DUPLICATE", "Số phiếu nhập đã tồn tại.")
            if await self._repo.active_supplier(payload.supplier_id) is None:
                raise BusinessError(422, "ERR-SUPPLIER-INACTIVE", "Nhà cung cấp không tồn tại hoặc đã ngừng hoạt động.")
            if await self._repo.active_warehouse(payload.warehouse_id) is None:
                raise BusinessError(422, "ERR-WAREHOUSE-INACTIVE", "Kho nhận không tồn tại hoặc đã ngừng hoạt động.")
            for line in payload.lines:
                if await self._repo.active_sku(line.sku_id) is None:
                    raise BusinessError(422, "ERR-SKU-INACTIVE", f"SKU {line.sku_id} không tồn tại hoặc đã ngừng kinh doanh.")
            total_amount = sum((line.qty_document * line.unit_cost for line in payload.lines), Decimal("0"))
            order = PurchaseOrder(
                po_id=payload.po_id, po_date=payload.po_date, supplier_id=payload.supplier_id,
                warehouse_id=payload.warehouse_id, status=DocumentStatus.DRAFT.value,
                total_amount=total_amount, created_by=self._actor_id,
            )
            lines = [PurchaseOrderLine(po_id=order.po_id, **line.model_dump()) for line in payload.lines]
            self._repo.add(order)
            for line in lines:
                self._repo.add(line)
            self._audit.record(
                entity_type="PURCHASE_ORDER", entity_id=order.po_id, action="CREATE", performed_by=self._actor_id,
                after={"status": order.status, "supplier_id": order.supplier_id, "warehouse_id": order.warehouse_id, "total_amount": total_amount},
            )
        return self._response(order, lines)

    async def submit_qc(self, po_id: str, payload: QcSubmission) -> PurchaseOrderResponse:
        await self._start_write()
        async with self._session.begin():
            order = await self._repo.get_order(po_id)
            if order is None:
                raise BusinessError(404, "ERR-PO-404", "Không tìm thấy phiếu nhập.")
            if order.status != DocumentStatus.DRAFT.value:
                raise BusinessError(409, "ERR-PO-STATE", "Chỉ được QC phiếu nhập ở trạng thái DRAFT.")
            lines = await self._repo.lines(po_id)
            qc_by_sku = {line.sku_id: line for line in payload.lines}
            expected_skus = {line.sku_id for line in lines}
            if set(qc_by_sku) != expected_skus:
                raise BusinessError(422, "ERR-QC-LINES", "Dữ liệu QC phải bao gồm đầy đủ và đúng các dòng SKU của phiếu.")
            before = []
            after = []
            for line in lines:
                qc = qc_by_sku[line.sku_id]
                qty_actual = qc.qty_actual_good + qc.qty_actual_damaged
                if qty_actual != line.qty_document and not qc.variance_reason:
                    raise BusinessError(422, "ERR-QC-VARIANCE", f"SKU {line.sku_id} có chênh lệch, bắt buộc nhập lý do.")
                before.append({"sku_id": line.sku_id, "good": line.qty_actual_good, "damaged": line.qty_actual_damaged})
                line.qty_actual_good = qc.qty_actual_good
                line.qty_actual_damaged = qc.qty_actual_damaged
                line.variance_reason = qc.variance_reason
                line.damaged_reason = qc.damaged_reason
                after.append({"sku_id": line.sku_id, "good": line.qty_actual_good, "damaged": line.qty_actual_damaged, "variance_reason": line.variance_reason, "damaged_reason": qc.damaged_reason})
            self._audit.record(entity_type="PURCHASE_ORDER", entity_id=po_id, action="UPDATE", performed_by=self._actor_id, before={"lines": before}, after={"qc_completed": True, "lines": after}, reason="Ghi nhận QC GOOD/DAMAGED")
        return self._response(order, lines)

    async def approve_order(self, po_id: str) -> PurchaseOrderResponse:
        await self._start_write()
        async with self._session.begin():
            order = await self._repo.get_order(po_id)
            if order is None:
                raise BusinessError(404, "ERR-PO-404", "Không tìm thấy phiếu nhập.")
            lines = await self._repo.lines(po_id)
            before_status = order.status
            final_approval = False
            # Approval workflow for Purchase Order: Accountant -> Warehouse Manager (final)
            if order.status == DocumentStatus.DRAFT.value and self._actor_role == RoleId.ACCOUNTANT.value:
                # level-1 approval moves to pending L2
                order.status = DocumentStatus.PENDING_L2.value
            elif order.status == DocumentStatus.PENDING_L2.value and self._actor_role == RoleId.WAREHOUSE_MANAGER.value:
                # final approval: validate QC and apply inbound stock changes
                self._ensure_qc_complete(lines)
                order.status = DocumentStatus.APPROVED.value
                final_approval = True
            else:
                raise BusinessError(403, "ERR-APPROVAL-001", "Bạn không có quyền duyệt ở bước này hoặc phiếu sai trạng thái.")
            if final_approval:
                # prepare quantities as list of (sku_id, status, qty)
                quantities: list[tuple[str, str, int]] = []
                for line in lines:
                    if line.qty_actual_good and line.qty_actual_good > 0:
                        quantities.append((line.sku_id, "GOOD", line.qty_actual_good))
                    if line.qty_actual_damaged and line.qty_actual_damaged > 0:
                        quantities.append((line.sku_id, "DAMAGED", line.qty_actual_damaged))
                await InventoryService(self._session).apply_inbound(
                    warehouse_id=order.warehouse_id,
                    quantities=quantities,
                    actor_id=self._actor_id,
                    reference_id=po_id,
                )
            self._audit.record(entity_type="PURCHASE_ORDER", entity_id=po_id, action="APPROVE", performed_by=self._actor_id, before={"status": before_status}, after={"status": order.status}, reason="Duyệt phiếu nhập" if not final_approval else "Duyệt cuối và tăng tồn")
        return self._response(order, lines)
