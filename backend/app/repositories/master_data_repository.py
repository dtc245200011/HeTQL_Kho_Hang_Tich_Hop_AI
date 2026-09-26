"""Data Access Layer cho Master Data, không chứa business rule."""
from typing import Any, TypeVar

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.models.business import BomComponent, ComboProduct, ProductModel, SkuVariant, Supplier
from app.models.core import StockLedger, Warehouse

ModelType = TypeVar("ModelType")


class MasterDataRepository:
    def __init__(self, session: AsyncSession) -> None:
        self.session = session

    async def get(self, model: type[ModelType], entity_id: str) -> ModelType | None:
        return await self.session.get(model, entity_id)

    async def list_active(self, model: type[ModelType], active_only: bool = True) -> list[ModelType]:
        statement = select(model)
        if active_only and hasattr(model, "is_active"):
            statement = statement.where(model.is_active.is_(True))  # type: ignore[attr-defined]
        return list((await self.session.scalars(statement)).all())

    async def supplier_by_tax_code(self, tax_code: str, excluded_id: str | None = None) -> Supplier | None:
        statement = select(Supplier).where(Supplier.tax_code == tax_code)
        if excluded_id:
            statement = statement.where(Supplier.supplier_id != excluded_id)
        return await self.session.scalar(statement)

    async def variant_exists(self, product_model_id: str, color: str, material: str) -> bool:
        statement = select(SkuVariant.sku_id).where(
            SkuVariant.product_model_id == product_model_id,
            SkuVariant.color == color,
            SkuVariant.material == material,
        )
        return await self.session.scalar(statement) is not None

    async def components(self, combo_id: str) -> list[BomComponent]:
        return list((await self.session.scalars(select(BomComponent).where(BomComponent.combo_id == combo_id))).all())

    async def good_stock(self, sku_id: str, warehouse_id: str) -> int:
        row = await self.session.get(StockLedger, (sku_id, warehouse_id, "GOOD"))
        return row.quantity if row else 0

    def add(self, entity: Any) -> None:
        self.session.add(entity)

    async def delete_components(self, combo_id: str) -> None:
        for component in await self.components(combo_id):
            await self.session.delete(component)
