"""Reporting utilities for inventory summaries (N-X-T) and related aggregations."""
from collections import defaultdict
from datetime import datetime, timezone
from typing import Iterable


def compute_inventory_summary_from_movements(movements: Iterable[dict], start: datetime, end: datetime) -> list[dict]:
	"""Compute opening, inbound, outbound, closing per (sku_id, warehouse_id).

	movements: iterable of dict with keys: sku_id, warehouse_id, quantity_delta (int), performed_at (datetime)
	start/end: datetime boundaries for the reporting period (inclusive)

	Returns list of dicts: {sku_id, warehouse_id, opening, inbound, outbound, closing}
	"""
	grouped = defaultdict(lambda: {"closing": 0, "inbound": 0, "outbound": 0})
	for m in movements:
		sku = m["sku_id"]
		wh = m["warehouse_id"]
		key = (sku, wh)
		qty = int(m["quantity_delta"])
		performed = m.get("performed_at")
		# closing: sum of all deltas up to end
		if performed is None or performed <= end:
			grouped[key]["closing"] += qty
		# inbound/outbound within [start, end]
		if performed is not None and start <= performed <= end:
			if qty > 0:
				grouped[key]["inbound"] += qty
			elif qty < 0:
				grouped[key]["outbound"] += -qty
	result = []
	for (sku, wh), vals in grouped.items():
		closing = vals["closing"]
		inbound = vals["inbound"]
		outbound = vals["outbound"]
		opening = closing - inbound + outbound
		result.append({
			"sku_id": sku,
			"warehouse_id": wh,
			"opening": opening,
			"inbound": inbound,
			"outbound": outbound,
			"closing": closing,
		})
	# sort for deterministic order
	result.sort(key=lambda x: (x["sku_id"], x["warehouse_id"]))
	return result
