"""Application service cho Kho, NCC, Product Model, SKU và Combo/BOM."""
from decimal import Decimal
import re
import unicodedata

from sqlalchemy.ext.asyncio import AsyncSession

from app.core.exceptions import BusinessError
from app.domain.policies.cbm import calculate_cbm
from app.models.business import BomComponent, ComboProduct, ProductModel, SkuVariant, Supplier
from app.models.core import Warehouse
from app.repositories.master_data_repository import MasterDataRepository
from app.schemas.master_data import (
    ComboCreate, ComboResponse, ComboUpdate, ProductModelCreate, ProductModelUpdate,
    SkuCreate, SkuUpdate, SupplierCreate, SupplierUpdate, WarehouseCreate, WarehouseUpdate,
)
from app.services.audit_service import AuditService


def _snapshot(entity: object, fields: tuple[str, ...]) -> dict[str, object]:
    return {field: getattr(entity, field) for field in fields}


def _slug(value: str) -> str:
    """Chuẩn hóa mã màu/chất liệu để tạo SKU có thể dùng ổn định."""
    normalized = unicodedata.normalize("NFKD", value).encode("ascii", "ignore").decode().upper()
    return re.sub(r"[^A-Z0-9]+", "-", normalized).strip("-")


class MasterDataService:
    def __init__(self, session: AsyncSession, actor_id: str) -> None:
        self._session = session
        self._repo = MasterDataRepository(session)
        self._audit = AuditService(session)
        self._actor_id = actor_id

    async def _commit(self) -> None:
        try:
            await self._session.commit()
        except Exception:
            await self._session.rollback()
            raise

    async def _get_or_404(self, model: type, entity_id: str, entity_type: str):
        entity = await self._repo.get(model, entity_id)
        if entity is None:
            raise BusinessError(404, "ERR-MASTER-404", f"Không tìm thấy {entity_type}: {entity_id}.")
        return entity

    async def list_warehouses(self) -> list[Warehouse]:
        return await self._repo.list_active(Warehouse)

    async def list_suppliers(self) -> list[Supplier]:
        return await self._repo.list_active(Supplier)

    async def list_product_models(self) -> list[ProductModel]:
        return await self._repo.list_active(ProductModel)

    async def list_skus(self) -> list[SkuVariant]:
        return await self._repo.list_active(SkuVariant)

    async def get_warehouse(self, warehouse_id: str) -> Warehouse:
        return await self._get_or_404(Warehouse, warehouse_id, "kho")

    async def get_supplier(self, supplier_id: str) -> Supplier:
        return await self._get_or_404(Supplier, supplier_id, "nhà cung cấp")

    async def get_product_model(self, product_model_id: str) -> ProductModel:
        return await self._get_or_404(ProductModel, product_model_id, "mã hàng gốc")

    async def get_sku(self, sku_id: str) -> SkuVariant:
        return await self._get_or_404(SkuVariant, sku_id, "SKU")

    async def create_warehouse(self, payload: WarehouseCreate) -> Warehouse:
        if await self._repo.get(Warehouse, payload.warehouse_id):
            raise BusinessError(409, "ERR-WAREHOUSE-DUPLICATE", "Mã kho đã tồn tại.")
        warehouse = Warehouse(**payload.model_dump())
        self._repo.add(warehouse)
        self._audit.record(entity_type="WAREHOUSE", entity_id=warehouse.warehouse_id, action="CREATE", performed_by=self._actor_id, after=_snapshot(warehouse, ("warehouse_id", "warehouse_name", "warehouse_type", "max_capacity_cbm", "is_active")))
        await self._commit()
        return warehouse

    async def update_warehouse(self, warehouse_id: str, payload: WarehouseUpdate) -> Warehouse:
        warehouse = await self._get_or_404(Warehouse, warehouse_id, "kho")
        fields = ("warehouse_id", "warehouse_name", "warehouse_type", "max_capacity_cbm", "is_active")
        before = _snapshot(warehouse, fields)
        for key, value in payload.model_dump().items():
            setattr(warehouse, key, value)
        self._audit.record(entity_type="WAREHOUSE", entity_id=warehouse_id, action="UPDATE", performed_by=self._actor_id, before=before, after=_snapshot(warehouse, fields))
        await self._commit()
        return warehouse

    async def deactivate_warehouse(self, warehouse_id: str) -> Warehouse:
        warehouse = await self._get_or_404(Warehouse, warehouse_id, "kho")
        before = _snapshot(warehouse, ("is_active",))
        warehouse.is_active = False
        self._audit.record(entity_type="WAREHOUSE", entity_id=warehouse_id, action="UPDATE", performed_by=self._actor_id, before=before, after={"is_active": False}, reason="Ngừng sử dụng kho")
        await self._commit()
        return warehouse

    async def create_supplier(self, payload: SupplierCreate) -> Supplier:
        if await self._repo.get(Supplier, payload.supplier_id):
            raise BusinessError(409, "ERR-SUPPLIER-DUPLICATE", "Mã nhà cung cấp đã tồn tại.")
        if await self._repo.supplier_by_tax_code(payload.tax_code):
            raise BusinessError(409, "ERR-SUPPLIER-TAX-DUPLICATE", "Mã số thuế đã tồn tại.")
        supplier = Supplier(**payload.model_dump())
        self._repo.add(supplier)
        self._audit.record(entity_type="SUPPLIER", entity_id=supplier.supplier_id, action="CREATE", performed_by=self._actor_id, after=_snapshot(supplier, ("supplier_id", "tax_code", "name", "is_active")))
        await self._commit()
        return supplier

    async def update_supplier(self, supplier_id: str, payload: SupplierUpdate) -> Supplier:
        supplier = await self._get_or_404(Supplier, supplier_id, "nhà cung cấp")
        if await self._repo.supplier_by_tax_code(payload.tax_code, supplier_id):
            raise BusinessError(409, "ERR-SUPPLIER-TAX-DUPLICATE", "Mã số thuế đã tồn tại.")
        fields = ("tax_code", "name", "address", "email", "phone", "is_active")
        before = _snapshot(supplier, fields)
        for key, value in payload.model_dump().items():
            setattr(supplier, key, value)
        self._audit.record(entity_type="SUPPLIER", entity_id=supplier_id, action="UPDATE", performed_by=self._actor_id, before=before, after=_snapshot(supplier, fields))
        await self._commit()
        return supplier

    async def deactivate_supplier(self, supplier_id: str) -> Supplier:
        supplier = await self._get_or_404(Supplier, supplier_id, "nhà cung cấp")
        supplier.is_active = False
        self._audit.record(entity_type="SUPPLIER", entity_id=supplier_id, action="UPDATE", performed_by=self._actor_id, after={"is_active": False}, reason="Ngừng hợp tác")
        await self._commit()
        return supplier

    async def create_product_model(self, payload: ProductModelCreate) -> ProductModel:
        if await self._repo.get(ProductModel, payload.product_model_id):
            raise BusinessError(409, "ERR-PRODUCT-DUPLICATE", "Mã hàng gốc đã tồn tại.")
        product = ProductModel(**payload.model_dump())
        self._repo.add(product)
        self._audit.record(entity_type="PRODUCT_MODEL", entity_id=product.product_model_id, action="CREATE", performed_by=self._actor_id, after=_snapshot(product, ("product_model_id", "product_name", "category", "unit", "min_stock", "is_active")))
        await self._commit()
        return product

    async def update_product_model(self, product_model_id: str, payload: ProductModelUpdate) -> ProductModel:
        product = await self._get_or_404(ProductModel, product_model_id, "mã hàng gốc")
        fields = ("product_name", "category", "unit", "min_stock", "is_active")
        before = _snapshot(product, fields)
        for key, value in payload.model_dump().items():
            setattr(product, key, value)
        self._audit.record(entity_type="PRODUCT_MODEL", entity_id=product_model_id, action="UPDATE", performed_by=self._actor_id, before=before, after=_snapshot(product, fields))
        await self._commit()
        return product

    async def deactivate_product_model(self, product_model_id: str) -> ProductModel:
        product = await self._get_or_404(ProductModel, product_model_id, "mã hàng gốc")
        product.is_active = False
        self._audit.record(entity_type="PRODUCT_MODEL", entity_id=product_model_id, action="UPDATE", performed_by=self._actor_id, after={"is_active": False}, reason="Ngừng kinh doanh")
        await self._commit()
        return product

    async def create_sku(self, payload: SkuCreate) -> SkuVariant:
        product = await self._get_or_404(ProductModel, payload.product_model_id, "mã hàng gốc")
        if not product.is_active:
            raise BusinessError(422, "ERR-PRODUCT-INACTIVE", "Không thể tạo SKU cho mã hàng gốc đã ngừng kinh doanh.")
        if await self._repo.variant_exists(payload.product_model_id, payload.color, payload.material):
            raise BusinessError(409, "ERR-SKU-DUPLICATE", "Tổ hợp màu sắc/chất liệu này đã tồn tại.")
        sku_id = f"{payload.product_model_id}-{_slug(payload.color)}-{_slug(payload.material)}"
        if len(sku_id) > 100:
            raise BusinessError(422, "ERR-SKU-ID", "Mã SKU sinh tự động vượt quá 100 ký tự.")
        sku = SkuVariant(**payload.model_dump(), sku_id=sku_id, cbm=calculate_cbm(payload.length_mm, payload.width_mm, payload.height_mm))
        self._repo.add(sku)
        self._audit.record(entity_type="SKU_VARIANT", entity_id=sku_id, action="CREATE", performed_by=self._actor_id, after=_snapshot(sku, ("sku_id", "product_model_id", "color", "material", "length_mm", "width_mm", "height_mm", "cbm", "unit_price", "is_active")))
        await self._commit()
        return sku

    async def update_sku(self, sku_id: str, payload: SkuUpdate) -> SkuVariant:
        sku = await self._get_or_404(SkuVariant, sku_id, "SKU")
        fields = ("length_mm", "width_mm", "height_mm", "cbm", "unit_price", "is_active")
        before = _snapshot(sku, fields)
        for key, value in payload.model_dump().items():
            setattr(sku, key, value)
        sku.cbm = calculate_cbm(sku.length_mm, sku.width_mm, sku.height_mm)
        self._audit.record(entity_type="SKU_VARIANT", entity_id=sku_id, action="UPDATE", performed_by=self._actor_id, before=before, after=_snapshot(sku, fields), reason="Cập nhật kích thước/giá SKU")
        await self._commit()
        return sku

    async def deactivate_sku(self, sku_id: str) -> SkuVariant:
        sku = await self._get_or_404(SkuVariant, sku_id, "SKU")
        sku.is_active = False
        self._audit.record(entity_type="SKU_VARIANT", entity_id=sku_id, action="UPDATE", performed_by=self._actor_id, after={"is_active": False}, reason="Ngừng kinh doanh")
        await self._commit()
        return sku

    async def _validated_components(self, combo_id: str, components) -> tuple[list[BomComponent], Decimal]:
        rows: list[BomComponent] = []
        calculated_cbm = Decimal("0")
        for component in components:
            sku = await self._get_or_404(SkuVariant, component.sku_id, "SKU cấu kiện")
            if not sku.is_active:
                raise BusinessError(422, "ERR-BOM-SKU-INACTIVE", f"SKU cấu kiện {sku.sku_id} đã ngừng kinh doanh.")
            rows.append(BomComponent(combo_id=combo_id, sku_id=sku.sku_id, quantity_per_set=component.quantity_per_set))
            calculated_cbm += sku.cbm * component.quantity_per_set
        return rows, calculated_cbm.quantize(Decimal("0.000001"))

    async def create_combo(self, payload: ComboCreate) -> ComboResponse:
        if await self._repo.get(ComboProduct, payload.combo_id):
            raise BusinessError(409, "ERR-COMBO-DUPLICATE", "Mã Combo đã tồn tại.")
        components, calculated_cbm = await self._validated_components(payload.combo_id, payload.components)
        combo = ComboProduct(combo_id=payload.combo_id, combo_name=payload.combo_name, cbm_override=payload.cbm_override, min_stock=payload.min_stock)
        self._repo.add(combo)
        for component in components:
            self._repo.add(component)
        self._audit.record(entity_type="COMBO", entity_id=combo.combo_id, action="CREATE", performed_by=self._actor_id, after={"combo_id": combo.combo_id, "combo_name": combo.combo_name, "components": [item.model_dump() for item in payload.components]})
        await self._commit()
        return ComboResponse(combo_id=combo.combo_id, combo_name=combo.combo_name, cbm_override=combo.cbm_override, calculated_cbm=calculated_cbm, min_stock=combo.min_stock, components=payload.components)

    async def get_combo(self, combo_id: str) -> ComboResponse:
        combo = await self._get_or_404(ComboProduct, combo_id, "Combo")
        components = await self._repo.components(combo_id)
        calculated_cbm = Decimal("0")
        payload_components = []
        for item in components:
            sku = await self._get_or_404(SkuVariant, item.sku_id, "SKU cấu kiện")
            calculated_cbm += sku.cbm * item.quantity_per_set
            payload_components.append({"sku_id": item.sku_id, "quantity_per_set": item.quantity_per_set})
        return ComboResponse(combo_id=combo.combo_id, combo_name=combo.combo_name, cbm_override=combo.cbm_override, calculated_cbm=calculated_cbm.quantize(Decimal("0.000001")), min_stock=combo.min_stock, components=payload_components)

    async def list_combos(self) -> list[ComboResponse]:
        """Danh sách Combo kèm BOM; không có cột tồn tĩnh trong phản hồi."""
        combos = await self._repo.list_active(ComboProduct, active_only=False)
        return [await self.get_combo(combo.combo_id) for combo in combos]

    async def update_combo(self, combo_id: str, payload: ComboUpdate) -> ComboResponse:
        combo = await self._get_or_404(ComboProduct, combo_id, "Combo")
        components, calculated_cbm = await self._validated_components(combo_id, payload.components)
        old_components = await self._repo.components(combo_id)
        before = {"combo_name": combo.combo_name, "cbm_override": combo.cbm_override, "min_stock": combo.min_stock, "components": [{"sku_id": item.sku_id, "quantity_per_set": item.quantity_per_set} for item in old_components]}
        await self._repo.delete_components(combo_id)
        combo.combo_name, combo.cbm_override, combo.min_stock = payload.combo_name, payload.cbm_override, payload.min_stock
        for component in components:
            self._repo.add(component)
        self._audit.record(entity_type="COMBO", entity_id=combo_id, action="UPDATE", performed_by=self._actor_id, before=before, after={"combo_name": combo.combo_name, "cbm_override": combo.cbm_override, "min_stock": combo.min_stock, "components": [item.model_dump() for item in payload.components]})
        await self._commit()
        return await self.get_combo(combo_id)

    async def combo_available_stock(self, combo_id: str, warehouse_id: str) -> int:
        await self._get_or_404(ComboProduct, combo_id, "Combo")
        await self._get_or_404(Warehouse, warehouse_id, "kho")
        components = await self._repo.components(combo_id)
        if not components:
            return 0
        # Tồn Combo suy diễn, chỉ tính GOOD, không bao giờ ghi vào stock_ledger.
        quantities = [await self._repo.good_stock(item.sku_id, warehouse_id) // item.quantity_per_set for item in components]
        return min(quantities)
