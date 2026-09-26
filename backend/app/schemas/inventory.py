"""DTO kiểm soát tồn kho và chuyển trạng thái hàng lỗi."""
from pydantic import BaseModel, ConfigDict, Field


class InventoryModel(BaseModel):
    model_config = ConfigDict(from_attributes=True, str_strip_whitespace=True)


class StockResponse(InventoryModel):
    sku_id: str
    warehouse_id: str
    status: str
    quantity: int


class DamageStockRequest(InventoryModel):
    sku_id: str = Field(min_length=1, max_length=100)
    warehouse_id: str = Field(min_length=1, max_length=50)
    quantity: int = Field(gt=0)
    reason: str = Field(min_length=1, max_length=500)
