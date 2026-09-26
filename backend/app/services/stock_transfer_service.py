"""Service for stock transfers: create and approve transfers.

Create: store draft transfer and lines.
Approve: only Warehouse Manager can approve -> perform transfer using InventoryService.transfer (atomic) and update status + audit.
"""
from datetime import datetime, timezone

from sqlalchemy.ext.asyncio import AsyncSession

from app.core.exceptions import BusinessError
from app.domain.enums import RoleId
from app.models.business import StockTransfer, StockTransferLine
from app.repositories.transfer_repository import TransferRepository
from app.schemas.operations import TransferCreate
from app.services.audit_service import AuditService
from app.services.inventory_service import InventoryService


class StockTransferService:
	def __init__(self, session: AsyncSession, actor_id: str, actor_role: str) -> None:
		self._session = session
		self._repo = TransferRepository(session)
		self._audit = AuditService(session)
		self._actor_id = actor_id
		self._actor_role = actor_role

	async def create_transfer(self, payload: TransferCreate) -> StockTransfer:
		if self._actor_role not in (RoleId.WAREHOUSE_KEEPER.value, RoleId.ADMIN.value, RoleId.WAREHOUSE_MANAGER.value):
			raise BusinessError(403, "ERR-ROLE-001", "Bạn không có quyền tạo chuyển kho.")
		async with self._session.begin():
			if await self._repo.get(payload.transfer_id):
				raise BusinessError(409, "ERR-TRANSFER-DUP", "ID chuyển kho đã tồn tại.")
			transfer = StockTransfer(
				transfer_id=payload.transfer_id,
				from_warehouse_id=payload.from_warehouse_id,
				to_warehouse_id=payload.to_warehouse_id,
				status="DRAFT",
				created_by=self._actor_id,
			)
			self._repo.add(transfer)
			lines = [StockTransferLine(transfer_id=transfer.transfer_id, sku_id=line.sku_id, qty=line.qty) for line in payload.lines]
			for l in lines:
				self._repo.add(l)
			await self._session.flush()
			self._audit.record(entity_type="STOCK_TRANSFER", entity_id=transfer.transfer_id, action="CREATE", performed_by=self._actor_id, after={"from": transfer.from_warehouse_id, "to": transfer.to_warehouse_id, "lines": [{"sku_id": l.sku_id, "qty": l.qty} for l in lines]})
		return transfer

	async def approve_transfer(self, transfer_id: str) -> StockTransfer:
		if self._actor_role != RoleId.WAREHOUSE_MANAGER.value and self._actor_role != RoleId.ADMIN.value:
			raise BusinessError(403, "ERR-APPROVAL-001", "Bạn không có quyền duyệt chuyển kho.")
		await self._session.commit() if self._session.in_transaction() else None
		async with self._session.begin():
			transfer = await self._repo.get(transfer_id)
			if not transfer:
				raise BusinessError(404, "ERR-TRANSFER-404", "Không tìm thấy chuyển kho.")
			if transfer.status == "APPROVED":
				raise BusinessError(409, "ERR-TRANSFER-STATE", "Chuyển kho đã được duyệt.")
			lines = await self._repo.lines(transfer_id)
			# Build and perform transfers per SKU
			inv = InventoryService(self._session)
			for line in lines:
				await inv.transfer(sku_id=line.sku_id, from_warehouse_id=transfer.from_warehouse_id, to_warehouse_id=transfer.to_warehouse_id, quantity=line.qty, reference_id=transfer.transfer_id, actor_id=self._actor_id)
			transfer.status = "APPROVED"
			self._audit.record(entity_type="STOCK_TRANSFER", entity_id=transfer.transfer_id, action="APPROVE", performed_by=self._actor_id, before={"status": "DRAFT"}, after={"status": "APPROVED"})
		return transfer
