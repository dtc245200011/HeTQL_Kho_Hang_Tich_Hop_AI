"""DTO cho nhập kho và kiểm tra chất lượng QC."""
from datetime import date
from decimal import Decimal

from pydantic import BaseModel, ConfigDict, Field, model_validator


class InboundModel(BaseModel):
    model_config = ConfigDict(from_attributes=True, str_strip_whitespace=True)


class PurchaseOrderLineCreate(InboundModel):
    sku_id: str = Field(min_length=1, max_length=100)
    qty_document: int = Field(gt=0)
    unit_cost: Decimal = Field(ge=0, max_digits=18, decimal_places=2)


class PurchaseOrderCreate(InboundModel):
    po_id: str = Field(min_length=1, max_length=50, pattern=r"^[A-Za-z0-9_-]+$")
    po_date: date
    supplier_id: str = Field(min_length=1, max_length=50)
    warehouse_id: str = Field(min_length=1, max_length=50)
    lines: list[PurchaseOrderLineCreate] = Field(min_length=1)

    @model_validator(mode="after")
    def unique_skus(self) -> "PurchaseOrderCreate":
        if len({line.sku_id for line in self.lines}) != len(self.lines):
            raise ValueError("Một SKU chỉ được xuất hiện một lần trong phiếu nhập.")
        return self


class QcLineInput(InboundModel):
    sku_id: str = Field(min_length=1, max_length=100)
    qty_actual_good: int = Field(ge=0)
    qty_actual_damaged: int = Field(ge=0)
    variance_reason: str | None = Field(default=None, max_length=500)
    damaged_reason: str | None = Field(default=None, max_length=500)

    @model_validator(mode="after")
    def damaged_requires_reason(self) -> "QcLineInput":
        if self.qty_actual_damaged > 0 and not self.damaged_reason:
            raise ValueError("Bắt buộc nhập damaged_reason khi có hàng DAMAGED.")
        return self


class QcSubmission(InboundModel):
    lines: list[QcLineInput] = Field(min_length=1)

    @model_validator(mode="after")
    def unique_skus(self) -> "QcSubmission":
        if len({line.sku_id for line in self.lines}) != len(self.lines):
            raise ValueError("SKU QC bị trùng.")
        return self


class PurchaseOrderLineResponse(InboundModel):
    sku_id: str
    qty_document: int
    qty_actual_good: int
    qty_actual_damaged: int
    unit_cost: Decimal
    variance_reason: str | None
    damaged_reason: str | None


class PurchaseOrderResponse(InboundModel):
    po_id: str
    po_date: date
    supplier_id: str
    warehouse_id: str
    status: str
    total_amount: Decimal
    created_by: str
    lines: list[PurchaseOrderLineResponse]
