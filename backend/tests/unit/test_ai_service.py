import pytest
import asyncio
from types import SimpleNamespace
from datetime import datetime, timezone
import json

from app.services.ai_service import AiService
from app.models.business import AiSuggestion, PurchaseOrder, PurchaseOrderLine


class FakeSession:
	def __init__(self):
		self.added = []
		self._in_tx = False

	def in_transaction(self):
		return self._in_tx

	async def __aenter__(self):
		return self

	async def __aexit__(self, exc_type, exc, tb):
		return False

	def begin(self):
		# return async context manager
		class Ctx:
			def __init__(self, outer):
				self.outer = outer
			async def __aenter__(self):
				return self.outer
			async def __aexit__(self, exc_type, exc, tb):
				return False
		return Ctx(self)

	async def execute(self, stmt):
		# used by generate_replenishment to fetch rows
		return SimpleNamespace(all=(lambda: self._rows) if hasattr(self, '_rows') else (lambda: []))

	async def get(self, model, id):
		# return None by default; tests can extend FakeSession to provide fixtures
		return None

	def add(self, obj):
		self.added.append(obj)

	# helper to set rows returned by execute()
	def set_rows(self, rows):
		self._rows = rows


class FakeAudit:
	def __init__(self):
		self.records = []
	def record(self, **kwargs):
		self.records.append(kwargs)


class FakeRepo:
	def __init__(self, suggestion=None):
		self._suggestion = suggestion
		self.added = []
	async def get(self, suggestion_id):
		return self._suggestion
	def add(self, entity):
		self.added.append(entity)
	async def list(self, status=None):
		return [self._suggestion] if self._suggestion and self._suggestion.status == status else []


@pytest.mark.asyncio
async def test_generate_replenishment_creates_suggestions(monkeypatch):
	session = FakeSession()
	# create fake sku/product/stock objects
	sku = SimpleNamespace(sku_id='SKU-A')
	product = SimpleNamespace(min_stock=10)
	stock = SimpleNamespace(warehouse_id='WH1', quantity=5)
	session.set_rows([(sku, product, stock)])
	# monkeypatch repository and audit to record adds
	service = AiService(session, actor_id='U1', actor_role='ADMIN')
	# replace internal repo and audit to use fake ones
	fake_repo = FakeRepo()
	service._repo = fake_repo
	service._audit = FakeAudit()
	items = await service.generate_replenishment()
	assert len(items) == 1
	assert items[0].sku_id == 'SKU-A'
	assert items[0].status == 'PENDING_REVIEW'


@pytest.mark.asyncio
async def test_approve_creates_po(monkeypatch):
	# prepare suggestion
	payload = {'warehouse_id': 'WH1', 'suggested_qty': 7}
	suggestion = AiSuggestion(suggestion_id='AI-1', suggestion_type='REPLENISHMENT', sku_id='SKU-X', payload_json=json.dumps(payload), status='PENDING_REVIEW')
	fake_repo = FakeRepo(suggestion=suggestion)
	session = FakeSession()
	service = AiService(session, actor_id='U1', actor_role='WAREHOUSE_MANAGER')
	service._repo = fake_repo
	service._audit = FakeAudit()
	# call approve
	item = await service.approve('AI-1')
	# should have created a PurchaseOrder and a PurchaseOrderLine in session.added
	po = next((x for x in session.added if isinstance(x, PurchaseOrder)), None)
	pol = next((x for x in session.added if isinstance(x, PurchaseOrderLine)), None)
	assert item.status == 'APPROVED'
	assert po is not None
	assert pol is not None
	assert pol.qty_document == 7


@pytest.mark.asyncio
async def test_reject_changes_status(monkeypatch):
	suggestion = AiSuggestion(suggestion_id='AI-2', suggestion_type='REPLENISHMENT', sku_id='SKU-Y', payload_json='{}', status='PENDING_REVIEW')
	fake_repo = FakeRepo(suggestion=suggestion)
	session = FakeSession()
	service = AiService(session, actor_id='U2', actor_role='WAREHOUSE_MANAGER')
	service._repo = fake_repo
	service._audit = FakeAudit()
	item = await service.reject('AI-2', reason='not needed')
	assert item.status == 'REJECTED'
	assert item.reviewed_by == 'U2'

