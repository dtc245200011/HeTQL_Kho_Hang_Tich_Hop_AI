"""Điểm vào duy nhất cho biến động tồn kho.

Các method thay đổi tồn sẽ được triển khai cùng inbound/outbound. Không service
nào khác được cập nhật StockLedger trực tiếp.
"""
from sqlalchemy import select, text
from sqlalchemy.exc import DBAPIError
from sqlalchemy.ext.asyncio import AsyncSession

from app.core.config import get_settings
from app.core.exceptions import BusinessError
from app.models.core import StockLedger, StockMovement
from app.services.audit_service import AuditService


class InventoryService:
    def __init__(self, session: AsyncSession) -> None:
        self._session = session

    def _movement(self, *, sku_id: str, warehouse_id: str, stock_status: str, delta: int, movement_type: str, reference_type: str, reference_id: str, actor_id: str) -> None:
        """Ghi immutable movement trong đúng transaction với stock_ledger."""
        self._session.add(StockMovement(sku_id=sku_id, warehouse_id=warehouse_id, stock_status=stock_status, quantity_delta=delta, movement_type=movement_type, reference_type=reference_type, reference_id=reference_id, performed_by=actor_id))

    async def apply_inbound(
        self, *, warehouse_id: str, quantities: list[tuple[str, str, int]], actor_id: str, reference_id: str
    ) -> None:
        """Tăng tồn sau khi phiếu nhập được duyệt cuối.

        `quantities` gồm (sku_id, GOOD|DAMAGED, quantity). Caller phải mở một
        transaction bao trùm cả đổi trạng thái phiếu và lời gọi này.
        """
        ordered = sorted((item for item in quantities if item[2] > 0), key=lambda item: (item[0], warehouse_id, item[1]))
        if not ordered:
            return
        await self._session.execute(text("SET LOCK_TIMEOUT :timeout_ms"), {"timeout_ms": get_settings().lock_timeout_seconds * 1000})
        audit = AuditService(self._session)
        try:
            for sku_id, stock_status, qty in ordered:
                statement = (
                    select(StockLedger)
                    .where(StockLedger.sku_id == sku_id, StockLedger.warehouse_id == warehouse_id, StockLedger.status == stock_status)
                    .with_hint(StockLedger, "WITH (UPDLOCK, ROWLOCK)", dialect_name="mssql")
                )
                ledger = await self._session.scalar(statement)
                if ledger is None:
                    ledger = StockLedger(sku_id=sku_id, warehouse_id=warehouse_id, status=stock_status, quantity=0)
                    self._session.add(ledger)
                    await self._session.flush()
                before = {"quantity": ledger.quantity, "status": stock_status, "warehouse_id": warehouse_id}
                ledger.quantity += qty
                self._movement(sku_id=sku_id, warehouse_id=warehouse_id, stock_status=stock_status, delta=qty, movement_type="INBOUND", reference_type="PURCHASE_ORDER", reference_id=reference_id, actor_id=actor_id)
                audit.record(
                    entity_type="STOCK_LEDGER", entity_id=f"{sku_id}:{warehouse_id}:{stock_status}", action="UPDATE",
                    performed_by=actor_id, before=before, after={**before, "quantity": ledger.quantity},
                    reason=f"Nhập kho từ phiếu {reference_id}",
                )
        except DBAPIError as exc:
            if 1222 in getattr(exc.orig, "args", []):
                raise BusinessError(409, "ERR-LOCK-001", "Mặt hàng đang được xử lý bởi giao dịch khác.") from exc
            raise

    async def apply_outbound(self, *, warehouse_id: str, requirements: dict[str, int], actor_id: str, reference_id: str) -> None:
        """Trừ tồn GOOD đã khóa; kiểm tra toàn bộ trước khi trừ để tránh xuất một phần."""
        keys = [(sku_id, warehouse_id, "GOOD") for sku_id in sorted(requirements)]
        try:
            ledgers = await self.lock_ledger_rows(keys)
        except BusinessError as exc:
            if exc.detail.get("code") == "ERR-STOCK-404":
                raise BusinessError(409, "ERR-STOCK-001", "Số lượng xuất vượt quá tồn khả dụng.") from exc
            raise
        by_sku = {ledger.sku_id: ledger for ledger in ledgers}
        for sku_id, required_qty in requirements.items():
            if by_sku[sku_id].quantity < required_qty:
                raise BusinessError(409, "ERR-STOCK-001", f"SKU {sku_id} không đủ tồn khả dụng.")
        audit = AuditService(self._session)
        for sku_id, required_qty in requirements.items():
            ledger = by_sku[sku_id]
            before_qty = ledger.quantity
            ledger.quantity -= required_qty
            self._movement(sku_id=sku_id, warehouse_id=warehouse_id, stock_status="GOOD", delta=-required_qty, movement_type="OUTBOUND", reference_type="SALES_ORDER", reference_id=reference_id, actor_id=actor_id)
            audit.record(entity_type="STOCK_LEDGER", entity_id=f"{sku_id}:{warehouse_id}:GOOD", action="UPDATE", performed_by=actor_id, before={"quantity": before_qty}, after={"quantity": ledger.quantity}, reason=f"Xuất kho từ phiếu {reference_id}")

    async def mark_damaged(self, *, sku_id: str, warehouse_id: str, quantity: int, reason: str, actor_id: str) -> None:
        """Chuyển GOOD sang DAMAGED nguyên tử sau khi Quản lý kho duyệt."""
        if quantity <= 0:
            raise BusinessError(422, "ERR-DAMAGED-QTY", "Số lượng hàng lỗi phải lớn hơn 0.")
        keys = [(sku_id, warehouse_id, "GOOD")]
        try:
            rows = await self.lock_ledger_rows(keys)
        except BusinessError as exc:
            if exc.detail.get("code") == "ERR-STOCK-404":
                raise BusinessError(409, "ERR-STOCK-001", "Không đủ tồn GOOD để chuyển sang DAMAGED.") from exc
            raise
        good = rows[0]
        damaged = await self._session.scalar(
            select(StockLedger)
            .where(StockLedger.sku_id == sku_id, StockLedger.warehouse_id == warehouse_id, StockLedger.status == "DAMAGED")
            .with_hint(StockLedger, "WITH (UPDLOCK, ROWLOCK)", dialect_name="mssql")
        )
        if damaged is None:
            damaged = StockLedger(sku_id=sku_id, warehouse_id=warehouse_id, status="DAMAGED", quantity=0)
            self._session.add(damaged)
            await self._session.flush()
        if good.quantity < quantity:
            raise BusinessError(409, "ERR-STOCK-001", "Không đủ tồn GOOD để chuyển sang DAMAGED.")
        audit = AuditService(self._session)
        good_before, damaged_before = good.quantity, damaged.quantity
        good.quantity -= quantity
        damaged.quantity += quantity
        self._movement(sku_id=sku_id, warehouse_id=warehouse_id, stock_status="GOOD", delta=-quantity, movement_type="DAMAGED", reference_type="DAMAGE_REPORT", reference_id=f"DMG-{sku_id}", actor_id=actor_id)
        self._movement(sku_id=sku_id, warehouse_id=warehouse_id, stock_status="DAMAGED", delta=quantity, movement_type="DAMAGED", reference_type="DAMAGE_REPORT", reference_id=f"DMG-{sku_id}", actor_id=actor_id)
        # Record audit for both affected ledgers
        audit.record(entity_type="STOCK_LEDGER", entity_id=f"{sku_id}:{warehouse_id}:GOOD", action="UPDATE", performed_by=actor_id, before={"quantity": good_before}, after={"quantity": good.quantity}, reason=f"Ghi nhận hàng lỗi: {reason}")
        audit.record(entity_type="STOCK_LEDGER", entity_id=f"{sku_id}:{warehouse_id}:DAMAGED", action="UPDATE", performed_by=actor_id, before={"quantity": damaged_before}, after={"quantity": damaged.quantity}, reason=f"Ghi nhận hàng lỗi: {reason}")
        audit.record(entity_type="STOCK_LEDGER", entity_id=f"{sku_id}:{warehouse_id}:DAMAGED", action="UPDATE", performed_by=actor_id, before={"quantity": damaged_before}, after={"quantity": damaged.quantity}, reason=reason)

    async def transfer_good(self, *, sku_id: str, from_warehouse_id: str, to_warehouse_id: str, quantity: int, actor_id: str, reference_id: str) -> None:
        """Chuyển GOOD giữa hai kho trong một transaction, khóa theo kho tăng dần."""
        if from_warehouse_id == to_warehouse_id:
            raise BusinessError(422, "ERR-TRANSFER-WAREHOUSE", "Kho nguồn và kho đích phải khác nhau.")
        if quantity <= 0:
            raise BusinessError(422, "ERR-TRANSFER-QTY", "Số lượng chuyển phải lớn hơn 0.")
        # Tạo bản ghi đích trước, rồi khóa cả hai theo warehouse_id tăng dần.
        destination = await self._session.scalar(select(StockLedger).where(StockLedger.sku_id == sku_id, StockLedger.warehouse_id == to_warehouse_id, StockLedger.status == "GOOD"))
        if destination is None:
            destination = StockLedger(sku_id=sku_id, warehouse_id=to_warehouse_id, status="GOOD", quantity=0)
            self._session.add(destination)
            await self._session.flush()
        rows = await self.lock_ledger_rows([
            (sku_id, from_warehouse_id, "GOOD"),
            (sku_id, to_warehouse_id, "GOOD"),
        ])
        by_warehouse = {row.warehouse_id: row for row in rows}
        source, destination = by_warehouse[from_warehouse_id], by_warehouse[to_warehouse_id]
        if source.quantity < quantity:
            raise BusinessError(409, "ERR-STOCK-001", "Không đủ tồn GOOD tại kho nguồn.")
        source_before, destination_before = source.quantity, destination.quantity
        source.quantity -= quantity
        destination.quantity += quantity
        self._movement(sku_id=sku_id, warehouse_id=from_warehouse_id, stock_status="GOOD", delta=-quantity, movement_type="TRANSFER_OUT", reference_type="STOCK_TRANSFER", reference_id=reference_id, actor_id=actor_id)
        self._movement(sku_id=sku_id, warehouse_id=to_warehouse_id, stock_status="GOOD", delta=quantity, movement_type="TRANSFER_IN", reference_type="STOCK_TRANSFER", reference_id=reference_id, actor_id=actor_id)
        audit = AuditService(self._session)
        for ledger, before in ((source, source_before), (destination, destination_before)):
            audit.record(entity_type="STOCK_LEDGER", entity_id=f"{sku_id}:{ledger.warehouse_id}:GOOD", action="UPDATE", performed_by=actor_id, before={"quantity": before}, after={"quantity": ledger.quantity}, reason=f"Chuyển kho {reference_id}")

    async def lock_ledger_rows(
        self, keys: list[tuple[str, str, str]]
    ) -> list[StockLedger]:
        """Khóa các dòng tồn theo thứ tự cố định tại cấp SQL Server.

        Hàm phải được gọi bên trong `async with session.begin()`. Lock chỉ tồn tại
        đến commit/rollback và không được truyền qua request HTTP khác.
        """
        ordered_keys = sorted(set(keys), key=lambda item: (item[0], item[1], item[2]))
        if not ordered_keys:
            return []
        try:
            await self._session.execute(
                text("SET LOCK_TIMEOUT :timeout_ms"),
                {"timeout_ms": get_settings().lock_timeout_seconds * 1000},
            )
            rows: list[StockLedger] = []
            for sku_id, warehouse_id, stock_status in ordered_keys:
                statement = (
                    select(StockLedger)
                    .where(
                        StockLedger.sku_id == sku_id,
                        StockLedger.warehouse_id == warehouse_id,
                        StockLedger.status == stock_status,
                    )
                    .with_hint(StockLedger, "WITH (UPDLOCK, ROWLOCK)", dialect_name="mssql")
                )
                row = await self._session.scalar(statement)
                if row is None:
                    raise BusinessError(404, "ERR-STOCK-404", "Không tìm thấy bản ghi tồn kho cần khóa.")
                rows.append(row)
            return rows
        except DBAPIError as exc:
            # SQL Server trả lỗi 1222 khi hết thời gian chờ khóa.
            if getattr(exc.orig, "args", [None])[0] == 1222:
                raise BusinessError(409, "ERR-LOCK-001", "Mặt hàng đang được xử lý bởi giao dịch khác.") from exc
            raise
