"""Các kiểm tra nghiệp vụ nhập kho chạy được cả khi chưa cài pytest."""
from pydantic import ValidationError

from app.core.exceptions import BusinessError
from app.models.business import PurchaseOrderLine
from app.schemas.inbound import QcLineInput
from app.services.inbound_service import InboundService


def test_damaged_qc_requires_reason() -> None:
    try:
        QcLineInput(sku_id="SKU-01", qty_actual_good=0, qty_actual_damaged=1)
    except ValidationError:
        return
    raise AssertionError("QC DAMAGED không có lý do phải bị từ chối.")


def test_approval_requires_completed_qc() -> None:
    line = PurchaseOrderLine(
        po_id="PO-01", sku_id="SKU-01", qty_document=2,
        qty_actual_good=0, qty_actual_damaged=0, unit_cost=1,
    )
    try:
        InboundService._ensure_qc_complete([line])
    except BusinessError as exc:
        assert exc.detail["code"] == "ERR-QC-INCOMPLETE"
        return
    raise AssertionError("Phiếu chưa QC không được duyệt.")


if __name__ == "__main__":
    test_damaged_qc_requires_reason()
    test_approval_requires_completed_qc()
    print("Inbound rules: OK")
