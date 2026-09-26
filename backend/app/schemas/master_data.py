"""DTO và validation cho toàn bộ Master Data."""
from decimal import Decimal

from pydantic import BaseModel, ConfigDict, Field, field_validator, model_validator

WAREHOUSE_TYPES = {"KHO_CHINH", "KHO_CHI_NHANH", "KHO_HANG_LOI"}


class ApiModel(BaseModel):
    model_config = ConfigDict(from_attributes=True, str_strip_whitespace=True)


class WarehouseCreate(ApiModel):
    warehouse_id: str = Field(min_length=1, max_length=50, pattern=r"^[A-Za-z0-9_-]+$")
    warehouse_name: str = Field(min_length=1, max_length=100)
    warehouse_type: str
    max_capacity_cbm: Decimal = Field(gt=0, max_digits=18, decimal_places=4)

    @field_validator("warehouse_type")
    @classmethod
    def validate_type(cls, value: str) -> str:
        if value not in WAREHOUSE_TYPES:
            raise ValueError("warehouse_type không hợp lệ.")
        return value


class WarehouseUpdate(ApiModel):
    warehouse_name: str = Field(min_length=1, max_length=100)
    warehouse_type: str
    max_capacity_cbm: Decimal = Field(gt=0, max_digits=18, decimal_places=4)

    _validate_type = field_validator("warehouse_type")(WarehouseCreate.validate_type.__func__)


class WarehouseResponse(WarehouseCreate):
    is_active: bool


class SupplierCreate(ApiModel):
    supplier_id: str = Field(min_length=1, max_length=50, pattern=r"^[A-Za-z0-9_-]+$")
    tax_code: str = Field(min_length=1, max_length=50)
    name: str = Field(min_length=1, max_length=200)
    address: str | None = Field(default=None, max_length=500)
    email: str | None = Field(default=None, max_length=100)
    phone: str | None = Field(default=None, max_length=20)


class SupplierUpdate(ApiModel):
    tax_code: str = Field(min_length=1, max_length=50)
    name: str = Field(min_length=1, max_length=200)
    address: str | None = Field(default=None, max_length=500)
    email: str | None = Field(default=None, max_length=100)
    phone: str | None = Field(default=None, max_length=20)


class SupplierResponse(SupplierCreate):
    is_active: bool


class ProductModelCreate(ApiModel):
    product_model_id: str = Field(min_length=1, max_length=50, pattern=r"^[A-Za-z0-9_-]+$")
    product_name: str = Field(min_length=1, max_length=200)
    category: str = Field(min_length=1, max_length=100)
    unit: str = Field(min_length=1, max_length=20)
    min_stock: int = Field(default=0, ge=0)


class ProductModelUpdate(ApiModel):
    product_name: str = Field(min_length=1, max_length=200)
    category: str = Field(min_length=1, max_length=100)
    unit: str = Field(min_length=1, max_length=20)
    min_stock: int = Field(ge=0)


class ProductModelResponse(ProductModelCreate):
    is_active: bool


class SkuCreate(ApiModel):
    product_model_id: str = Field(min_length=1, max_length=50)
    color: str = Field(min_length=1, max_length=50)
    material: str = Field(min_length=1, max_length=50)
    length_mm: int = Field(gt=0)
    width_mm: int = Field(gt=0)
    height_mm: int = Field(gt=0)
    unit_price: Decimal = Field(default=Decimal("0"), ge=0, max_digits=18, decimal_places=2)


class SkuUpdate(ApiModel):
    length_mm: int = Field(gt=0)
    width_mm: int = Field(gt=0)
    height_mm: int = Field(gt=0)
    unit_price: Decimal = Field(ge=0, max_digits=18, decimal_places=2)


class SkuResponse(ApiModel):
    sku_id: str
    product_model_id: str
    color: str
    material: str
    length_mm: int
    width_mm: int
    height_mm: int
    cbm: Decimal
    unit_price: Decimal
    is_active: bool


class BomComponentInput(ApiModel):
    sku_id: str = Field(min_length=1, max_length=100)
    quantity_per_set: int = Field(gt=0)


class ComboCreate(ApiModel):
    combo_id: str = Field(min_length=1, max_length=50, pattern=r"^[A-Za-z0-9_-]+$")
    combo_name: str = Field(min_length=1, max_length=200)
    cbm_override: Decimal | None = Field(default=None, gt=0, max_digits=18, decimal_places=6)
    min_stock: int = Field(default=0, ge=0)
    components: list[BomComponentInput] = Field(min_length=1)

    @model_validator(mode="after")
    def unique_component_skus(self) -> "ComboCreate":
        if len({item.sku_id for item in self.components}) != len(self.components):
            raise ValueError("Một SKU chỉ được xuất hiện một lần trong BOM.")
        return self


class ComboUpdate(ApiModel):
    combo_name: str = Field(min_length=1, max_length=200)
    cbm_override: Decimal | None = Field(default=None, gt=0, max_digits=18, decimal_places=6)
    min_stock: int = Field(ge=0)
    components: list[BomComponentInput] = Field(min_length=1)


class ComboResponse(ApiModel):
    combo_id: str
    combo_name: str
    cbm_override: Decimal | None
    calculated_cbm: Decimal
    min_stock: int
    components: list[BomComponentInput]


class ComboAvailabilityResponse(ApiModel):
    combo_id: str
    warehouse_id: str
    available_stock: int
