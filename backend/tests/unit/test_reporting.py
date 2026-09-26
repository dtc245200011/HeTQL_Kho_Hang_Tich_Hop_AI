from datetime import datetime, timedelta, timezone
from app.services.reporting_service import compute_inventory_summary_from_movements


def test_compute_inventory_summary_simple():
	now = datetime(2023, 1, 15, 12, 0, 0)
	start = datetime(2023, 1, 10)
	end = datetime(2023, 1, 20)
	movements = [
		# before period
		{"sku_id": "SKU1", "warehouse_id": "WH1", "quantity_delta": 10, "performed_at": datetime(2023, 1, 5)},
		# inbound inside period
		{"sku_id": "SKU1", "warehouse_id": "WH1", "quantity_delta": 5, "performed_at": datetime(2023, 1, 12)},
		# outbound inside period
		{"sku_id": "SKU1", "warehouse_id": "WH1", "quantity_delta": -3, "performed_at": datetime(2023, 1, 13)},
		# after period (should count for closing? performed_at <= end rule excludes after)
		{"sku_id": "SKU1", "warehouse_id": "WH1", "quantity_delta": 2, "performed_at": datetime(2023, 1, 25)},
		# different sku
		{"sku_id": "SKU2", "warehouse_id": "WH1", "quantity_delta": 7, "performed_at": datetime(2023, 1, 8)},
		{"sku_id": "SKU2", "warehouse_id": "WH1", "quantity_delta": -2, "performed_at": datetime(2023, 1, 15)},
	]
	summary = compute_inventory_summary_from_movements(movements, start, end)
	# Find SKU1
	s1 = next((s for s in summary if s['sku_id'] == 'SKU1' and s['warehouse_id'] == 'WH1'), None)
	assert s1 is not None
	# closing should include deltas up to end (exclude the one at 2023-01-25)
	assert s1['closing'] == 10 + 5 - 3
	# inbound inside period is 5
	assert s1['inbound'] == 5
	# outbound inside period is 3
	assert s1['outbound'] == 3
	# opening = closing - inbound + outbound
	assert s1['opening'] == s1['closing'] - s1['inbound'] + s1['outbound']

	s2 = next((s for s in summary if s['sku_id'] == 'SKU2'), None)
	assert s2 is not None
	assert s2['closing'] == 7 - 2
	assert s2['inbound'] == 0
	assert s2['outbound'] == 2


def test_empty_movements_returns_empty():
	start = datetime(2023, 1, 1)
	end = datetime(2023, 1, 31)
	assert compute_inventory_summary_from_movements([], start, end) == []
