"""Service for stock adjustments: create and approve adjustments.

Create: records a pending adjustment (no direct ledger change).
Approve: only Warehouse Manager can approve -> applies qty_delta to StockLedger using InventoryService and records audit.
"""
from datetime import datetime, timezone

from sqlalchemy.ext.asyncio import AsyncSession

from app.core.exceptions import BusinessError
from app.domain.enums import RoleId
from app.models.business import StockAdjustment
from app.repositories.adjustment_repository import AdjustmentRepository
from app.schemas.operations import AdjustmentCreate
from app.services.audit_service import AuditService
from app.services.inventory_service import InventoryService


class StockAdjustmentService:
	def __init__(self, session: AsyncSession, actor_id: str, actor_role: str) -> None:
		self._session = session
		self._repo = AdjustmentRepository(session)
		self._audit = AuditService(session)
		self._actor_id = actor_id
		self._actor_role = actor_role

	async def create_adjustment(self, payload: AdjustmentCreate) -> StockAdjustment:
		if self._actor_role not in (RoleId.WAREHOUSE_KEEPER.value, RoleId.ADMIN.value, RoleId.WAREHOUSE_MANAGER.value):
			raise BusinessError(403, "ERR-ROLE-001", "Bạn không có quyền tạo điều chỉnh tồn.")
		# For now adjustments are applied immediately and recorded (approved by creator).
		async with self._session.begin():
			adjustment_id = f"ADJ-{int(datetime.now(timezone.utc).timestamp() * 1000)}"
			inv = InventoryService(self._session)
			# apply change via InventoryService to ensure StockMovement and audit consistency
			if payload.qty_delta > 0:
				quantities = [(payload.sku_id, "GOOD", payload.qty_delta)]
				await inv.apply_inbound(warehouse_id=payload.warehouse_id, quantities=quantities, actor_id=self._actor_id, reference_id=adjustment_id)
			elif payload.qty_delta < 0:
				requirements = {payload.sku_id: abs(payload.qty_delta)}
				await inv.apply_outbound(warehouse_id=payload.warehouse_id, requirements=requirements, actor_id=self._actor_id, reference_id=adjustment_id)
			# record adjustment as approved by actor to match DB schema (approved_by not nullable)
			adj = StockAdjustment(
				adjustment_id=adjustment_id,
				sku_id=payload.sku_id,
				warehouse_id=payload.warehouse_id,
				qty_delta=payload.qty_delta,
				reason=payload.reason,
				approved_by=self._actor_id,
			)
			self._repo.add(adj)
			self._audit.record(entity_type="STOCK_ADJUSTMENT", entity_id=adjustment_id, action="APPROVE", performed_by=self._actor_id, after={"qty_delta": adj.qty_delta, "reason": adj.reason})
		return adj

	async def approve_adjustment(self, adjustment_id: str) -> StockAdjustment:
		if self._actor_role != RoleId.WAREHOUSE_MANAGER.value and self._actor_role != RoleId.ADMIN.value:
			raise BusinessError(403, "ERR-APPROVAL-001", "Bạn không có quyền duyệt điều chỉnh tồn.")
		await self._session.commit() if self._session.in_transaction() else None
		async with self._session.begin():
			adj = await self._repo.get(adjustment_id)
			if not adj:
				raise BusinessError(404, "ERR-ADJ-404", "Không tìm thấy phiếu điều chỉnh.")
			if adj.approved_by:
				raise BusinessError(409, "ERR-ADJ-STATE", "Phiếu đã được phê duyệt.")
			# apply adjustment atomically via InventoryService
			# positive qty_delta => increase GOOD stock; negative => decrease GOOD stock
			inv = InventoryService(self._session)
			if adj.qty_delta > 0:
				quantities = [(adj.sku_id, "GOOD", adj.qty_delta)]
				await inv.apply_inbound(warehouse_id=adj.warehouse_id, quantities=quantities, actor_id=self._actor_id, reference_id=adjustment_id)
			else:
				# For negative delta, treat as outbound adjustment (decrease GOOD)
				requirements = {adj.sku_id: abs(adj.qty_delta)}
				await inv.apply_outbound(warehouse_id=adj.warehouse_id, requirements=requirements, actor_id=self._actor_id, reference_id=adjustment_id)
			adj.approved_by = self._actor_id
			adj.reviewed_at = datetime.now(timezone.utc)
			self._audit.record(entity_type="STOCK_ADJUSTMENT", entity_id=adjustment_id, action="APPROVE", performed_by=self._actor_id, before={"qty_delta": adj.qty_delta}, after={"approved_by": adj.approved_by})
		return adj
