"""REST controller cho Master Data. Toàn bộ endpoint kiểm tra JWT/RBAC."""
from fastapi import APIRouter, Depends, status
from sqlalchemy.ext.asyncio import AsyncSession

from app.api.dependencies import get_current_user, require_roles
from app.core.database import get_db_session
from app.domain.enums import RoleId
from app.models.core import User
from app.schemas.master_data import (
    ComboAvailabilityResponse, ComboCreate, ComboResponse, ComboUpdate, ProductModelCreate,
    ProductModelResponse, ProductModelUpdate, SkuCreate, SkuResponse, SkuUpdate,
    SupplierCreate, SupplierResponse, SupplierUpdate, WarehouseCreate, WarehouseResponse,
    WarehouseUpdate,
)
from app.services.master_data_service import MasterDataService

router = APIRouter(tags=["Master Data"])
WRITE_ROLES = (RoleId.ADMIN.value, RoleId.WAREHOUSE_MANAGER.value, RoleId.WAREHOUSE_KEEPER.value)
MANAGER_ROLES = (RoleId.ADMIN.value, RoleId.WAREHOUSE_MANAGER.value)


def service_for(
    session: AsyncSession = Depends(get_db_session), current_user: User = Depends(get_current_user)
) -> MasterDataService:
    return MasterDataService(session, current_user.user_id)


@router.get("/warehouses", response_model=list[WarehouseResponse])
async def list_warehouses(service: MasterDataService = Depends(service_for)):
    return await service.list_warehouses()


@router.get("/warehouses/{warehouse_id}", response_model=WarehouseResponse)
async def get_warehouse(warehouse_id: str, service: MasterDataService = Depends(service_for)):
    return await service.get_warehouse(warehouse_id)


@router.post("/warehouses", response_model=WarehouseResponse, status_code=status.HTTP_201_CREATED)
async def create_warehouse(payload: WarehouseCreate, service: MasterDataService = Depends(service_for), _: User = Depends(require_roles(*MANAGER_ROLES))):
    return await service.create_warehouse(payload)


@router.put("/warehouses/{warehouse_id}", response_model=WarehouseResponse)
async def update_warehouse(warehouse_id: str, payload: WarehouseUpdate, service: MasterDataService = Depends(service_for), _: User = Depends(require_roles(*MANAGER_ROLES))):
    return await service.update_warehouse(warehouse_id, payload)


@router.post("/warehouses/{warehouse_id}/deactivate", response_model=WarehouseResponse)
async def deactivate_warehouse(warehouse_id: str, service: MasterDataService = Depends(service_for), _: User = Depends(require_roles(*MANAGER_ROLES))):
    return await service.deactivate_warehouse(warehouse_id)


@router.get("/suppliers", response_model=list[SupplierResponse])
async def list_suppliers(service: MasterDataService = Depends(service_for)):
    return await service.list_suppliers()


@router.get("/suppliers/{supplier_id}", response_model=SupplierResponse)
async def get_supplier(supplier_id: str, service: MasterDataService = Depends(service_for)):
    return await service.get_supplier(supplier_id)


@router.post("/suppliers", response_model=SupplierResponse, status_code=status.HTTP_201_CREATED)
async def create_supplier(payload: SupplierCreate, service: MasterDataService = Depends(service_for), _: User = Depends(require_roles(*WRITE_ROLES))):
    return await service.create_supplier(payload)


@router.put("/suppliers/{supplier_id}", response_model=SupplierResponse)
async def update_supplier(supplier_id: str, payload: SupplierUpdate, service: MasterDataService = Depends(service_for), _: User = Depends(require_roles(*WRITE_ROLES))):
    return await service.update_supplier(supplier_id, payload)


@router.post("/suppliers/{supplier_id}/deactivate", response_model=SupplierResponse)
async def deactivate_supplier(supplier_id: str, service: MasterDataService = Depends(service_for), _: User = Depends(require_roles(*MANAGER_ROLES))):
    return await service.deactivate_supplier(supplier_id)


@router.get("/product-models", response_model=list[ProductModelResponse])
async def list_product_models(service: MasterDataService = Depends(service_for)):
    return await service.list_product_models()


@router.get("/product-models/{product_model_id}", response_model=ProductModelResponse)
async def get_product_model(product_model_id: str, service: MasterDataService = Depends(service_for)):
    return await service.get_product_model(product_model_id)


@router.post("/product-models", response_model=ProductModelResponse, status_code=status.HTTP_201_CREATED)
async def create_product_model(payload: ProductModelCreate, service: MasterDataService = Depends(service_for), _: User = Depends(require_roles(*WRITE_ROLES))):
    return await service.create_product_model(payload)


@router.put("/product-models/{product_model_id}", response_model=ProductModelResponse)
async def update_product_model(product_model_id: str, payload: ProductModelUpdate, service: MasterDataService = Depends(service_for), _: User = Depends(require_roles(*WRITE_ROLES))):
    return await service.update_product_model(product_model_id, payload)


@router.post("/product-models/{product_model_id}/deactivate", response_model=ProductModelResponse)
async def deactivate_product_model(product_model_id: str, service: MasterDataService = Depends(service_for), _: User = Depends(require_roles(*MANAGER_ROLES))):
    return await service.deactivate_product_model(product_model_id)


@router.get("/skus", response_model=list[SkuResponse])
async def list_skus(service: MasterDataService = Depends(service_for)):
    return await service.list_skus()


@router.get("/skus/{sku_id}", response_model=SkuResponse)
async def get_sku(sku_id: str, service: MasterDataService = Depends(service_for)):
    return await service.get_sku(sku_id)


@router.post("/skus", response_model=SkuResponse, status_code=status.HTTP_201_CREATED)
async def create_sku(payload: SkuCreate, service: MasterDataService = Depends(service_for), _: User = Depends(require_roles(*WRITE_ROLES))):
    return await service.create_sku(payload)


@router.put("/skus/{sku_id}", response_model=SkuResponse)
async def update_sku(sku_id: str, payload: SkuUpdate, service: MasterDataService = Depends(service_for), _: User = Depends(require_roles(*WRITE_ROLES))):
    return await service.update_sku(sku_id, payload)


@router.post("/skus/{sku_id}/deactivate", response_model=SkuResponse)
async def deactivate_sku(sku_id: str, service: MasterDataService = Depends(service_for), _: User = Depends(require_roles(*MANAGER_ROLES))):
    return await service.deactivate_sku(sku_id)


@router.get("/combos/{combo_id}", response_model=ComboResponse)
async def get_combo(combo_id: str, service: MasterDataService = Depends(service_for)):
    return await service.get_combo(combo_id)


@router.get("/combos", response_model=list[ComboResponse])
async def list_combos(service: MasterDataService = Depends(service_for)):
    return await service.list_combos()


@router.post("/combos", response_model=ComboResponse, status_code=status.HTTP_201_CREATED)
async def create_combo(payload: ComboCreate, service: MasterDataService = Depends(service_for), _: User = Depends(require_roles(*WRITE_ROLES))):
    return await service.create_combo(payload)


@router.put("/combos/{combo_id}", response_model=ComboResponse)
async def update_combo(combo_id: str, payload: ComboUpdate, service: MasterDataService = Depends(service_for), _: User = Depends(require_roles(*WRITE_ROLES))):
    return await service.update_combo(combo_id, payload)


@router.get("/combos/{combo_id}/available-stock", response_model=ComboAvailabilityResponse)
async def combo_available_stock(combo_id: str, warehouse_id: str, service: MasterDataService = Depends(service_for)):
    available = await service.combo_available_stock(combo_id, warehouse_id)
    return ComboAvailabilityResponse(combo_id=combo_id, warehouse_id=warehouse_id, available_stock=available)
