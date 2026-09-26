"""Service for AI proposals (read-only suggestions + human-in-loop approval)."""
from datetime import datetime, date, timezone
from uuid import uuid4
import json
from decimal import Decimal

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.models.business import AiSuggestion, SkuVariant, ProductModel, PurchaseOrder, PurchaseOrderLine
from app.models.core import StockLedger
from app.repositories.ai_repository import AiRepository
from app.services.audit_service import AuditService
from app.core.exceptions import BusinessError
from app.domain.enums import RoleId


class AiService:
	def __init__(self, session: AsyncSession, actor_id: str, actor_role: str) -> None:
		self._session = session
		self._repo = AiRepository(session)
		self._audit = AuditService(session)
		self._actor_id = actor_id
		self._actor_role = actor_role

	async def generate_replenishment(self) -> list[AiSuggestion]:
		"""Generate replenishment suggestions by scanning SKUs whose available stock <= min_stock.
		AI engine is read-only in business data; suggestions are stored in ai_suggestions table for human review.
		"""
		async with self._session.begin():
			stmt = select(SkuVariant, ProductModel, StockLedger).join(ProductModel, SkuVariant.product_model_id == ProductModel.product_model_id).join(StockLedger, StockLedger.sku_id == SkuVariant.sku_id).where(StockLedger.status == 'GOOD', StockLedger.quantity <= ProductModel.min_stock)
			rows = (await self._session.execute(stmt)).all()
			result: list[AiSuggestion] = []
			for sku, product, stock in rows:
				payload = {"warehouse_id": stock.warehouse_id, "available_stock": int(stock.quantity), "min_stock": int(product.min_stock), "suggested_qty": max(int(product.min_stock * 2 - stock.quantity), 1)}
				suggestion = AiSuggestion(suggestion_id=f'AI-{uuid4().hex[:12].upper()}', suggestion_type='REPLENISHMENT', sku_id=sku.sku_id, payload_json=json.dumps(payload, ensure_ascii=False), status='PENDING_REVIEW')
				self._repo.add(suggestion)
				result.append(suggestion)
			self._audit.record(entity_type='AI_SUGGESTION', entity_id='BATCH', action='CREATE', performed_by=self._actor_id, after={'count': len(result)}, reason='Generate replenishment suggestions')
		return result

	async def list_pending(self) -> list[AiSuggestion]:
		return await self._repo.list('PENDING_REVIEW')

	async def approve(self, suggestion_id: str) -> AiSuggestion:
		if self._actor_role not in (RoleId.WAREHOUSE_MANAGER.value, RoleId.ADMIN.value):
			raise BusinessError(403, 'ERR-AI-APPROVE', 'Bạn không có quyền duyệt gợi ý AI.')
		if self._session.in_transaction():
			await self._session.commit()
		async with self._session.begin():
			item = await self._repo.get(suggestion_id)
			if not item or item.status != 'PENDING_REVIEW':
				raise BusinessError(409, 'ERR-AI-001', 'Gợi ý AI không tồn tại hoặc đã được xử lý.')
			item.status = 'APPROVED'
			item.reviewed_by = self._actor_id
			item.reviewed_at = datetime.now(timezone.utc)
			self._audit.record(entity_type='AI_SUGGESTION', entity_id=suggestion_id, action='APPROVE', performed_by=self._actor_id, before={'status': 'PENDING_REVIEW'}, after={'status': 'APPROVED'})
			# If the suggestion is a replenishment, create a draft PurchaseOrder for human review
			if item.suggestion_type == 'REPLENISHMENT':
				# payload_json expected to be JSON
				try:
					payload = json.loads(item.payload_json)
				except Exception:
					payload = {}
				warehouse_id = payload.get('warehouse_id') or None
				suggested_qty = int(payload.get('suggested_qty') or 0)
				# create minimal draft PO for human to review/edit
				po_id = f'PO-AI-{uuid4().hex[:10].upper()}'
				po = PurchaseOrder(po_id=po_id, po_date=date.today(), supplier_id='AUTO_GEN', warehouse_id=warehouse_id or '', status='DRAFT', total_amount=Decimal('0'), created_by=self._actor_id)
				self._session.add(po)
				if suggested_qty > 0:
					line = PurchaseOrderLine(po_id=po_id, sku_id=item.sku_id, qty_document=suggested_qty, qty_actual_good=0, qty_actual_damaged=0, unit_cost=Decimal('0'))
					self._session.add(line)
				self._audit.record(entity_type='PURCHASE_ORDER', entity_id=po_id, action='CREATE', performed_by=self._actor_id, after={'status': 'DRAFT', 'auto_generated_from_ai': suggestion_id})
		return item

	async def reject(self, suggestion_id: str, reason: str | None = None) -> AiSuggestion:
		if self._actor_role not in (RoleId.WAREHOUSE_MANAGER.value, RoleId.ADMIN.value):
			raise BusinessError(403, 'ERR-AI-REJECT', 'Bạn không có quyền từ chối gợi ý AI.')
		if self._session.in_transaction():
			await self._session.commit()
		async with self._session.begin():
			item = await self._repo.get(suggestion_id)
			if not item or item.status != 'PENDING_REVIEW':
				raise BusinessError(409, 'ERR-AI-001', 'Gợi ý AI không tồn tại hoặc đã được xử lý.')
			item.status = 'REJECTED'
			item.reviewed_by = self._actor_id
			item.reviewed_at = datetime.now(timezone.utc)
			self._audit.record(entity_type='AI_SUGGESTION', entity_id=suggestion_id, action='REJECT', performed_by=self._actor_id, before={'status': 'PENDING_REVIEW'}, after={'status': 'REJECTED'}, reason=reason)
		return item
