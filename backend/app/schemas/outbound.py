"""DTO cho phiếu xuất kho SKU hoặc Combo."""
from datetime import date
from decimal import Decimal

from pydantic import BaseModel, ConfigDict, Field, model_validator


class OutboundModel(BaseModel):
    model_config = ConfigDict(from_attributes=True, str_strip_whitespace=True)


class SalesOrderLineCreate(OutboundModel):
    sku_id: str | None = Field(default=None, max_length=100)
    combo_id: str | None = Field(default=None, max_length=50)
    qty: int = Field(gt=0)
    unit_price: Decimal = Field(ge=0, max_digits=18, decimal_places=2)

    @model_validator(mode="after")
    def exactly_one_item(self) -> "SalesOrderLineCreate":
        if bool(self.sku_id) == bool(self.combo_id):
            raise ValueError("Mỗi dòng xuất phải chọn đúng một SKU hoặc một Combo.")
        return self


class SalesOrderCreate(OutboundModel):
    so_id: str = Field(min_length=1, max_length=50, pattern=r"^[A-Za-z0-9_-]+$")
    so_date: date
    warehouse_id: str = Field(min_length=1, max_length=50)
    lines: list[SalesOrderLineCreate] = Field(min_length=1)


class SalesOrderLineResponse(SalesOrderLineCreate):
    line_id: int


class SalesOrderResponse(OutboundModel):
    so_id: str
    so_date: date
    warehouse_id: str
    status: str
    total_amount: Decimal
    created_by: str
    lines: list[SalesOrderLineResponse]
