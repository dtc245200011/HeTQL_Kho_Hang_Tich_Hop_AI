from datetime import date
from pydantic import BaseModel, ConfigDict, Field, model_validator


class OperationModel(BaseModel):
    model_config = ConfigDict(from_attributes=True, str_strip_whitespace=True)


class TransferLine(OperationModel):
    sku_id: str = Field(min_length=1, max_length=100)
    qty: int = Field(gt=0)


class TransferCreate(OperationModel):
    transfer_id: str = Field(min_length=1, max_length=50, pattern=r"^[A-Za-z0-9_-]+$")
    from_warehouse_id: str = Field(min_length=1, max_length=50)
    to_warehouse_id: str = Field(min_length=1, max_length=50)
    lines: list[TransferLine] = Field(min_length=1)

    @model_validator(mode="after")
    def warehouses_different(self) -> "TransferCreate":
        if self.from_warehouse_id == self.to_warehouse_id:
            raise ValueError("from_warehouse_id and to_warehouse_id must be different.")
        return self


class AdjustmentCreate(OperationModel):
    sku_id: str = Field(min_length=1, max_length=100)
    warehouse_id: str = Field(min_length=1, max_length=50)
    qty_delta: int = Field()
    reason: str = Field(min_length=1, max_length=500)


class AdjustmentResponse(OperationModel):
    adjustment_id: str
    sku_id: str
    warehouse_id: str
    qty_delta: int
    reason: str
    approved_by: str | None
    reviewed_at: date | None


class TransferLineResponse(OperationModel):
    sku_id: str
    qty: int


class TransferResponse(OperationModel):
    transfer_id: str
    from_warehouse_id: str
    to_warehouse_id: str
    status: str
    created_by: str
    lines: list[TransferLineResponse]


class NxtReportLine(OperationModel):
    sku_id: str
    warehouse_id: str
    opening: int
    inbound: int
    outbound: int
    closing: int


class AiSuggestionResponse(OperationModel):
    suggestion_id: str
    suggestion_type: str
    sku_id: str | None
    payload_json: str
    status: str
