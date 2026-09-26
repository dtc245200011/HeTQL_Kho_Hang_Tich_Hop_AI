"""Điểm khởi động FastAPI."""
from fastapi import FastAPI
from fastapi.responses import JSONResponse
from starlette.exceptions import HTTPException as StarletteHTTPException

from app.api.v1.auth import router as auth_router
from app.api.v1.master_data import router as master_data_router
from app.api.v1.inbound import router as inbound_router
from app.api.v1.outbound import router as outbound_router
from app.api.v1.inventory import router as inventory_router
from app.api.v1.operations import router as operations_router
from app.core.config import get_settings

settings = get_settings()
app = FastAPI(title=settings.app_name, debug=settings.debug, version="1.0.0")
app.include_router(auth_router, prefix="/api/v1")
app.include_router(master_data_router, prefix="/api/v1")
app.include_router(inbound_router, prefix="/api/v1")
app.include_router(outbound_router, prefix="/api/v1")
app.include_router(inventory_router, prefix="/api/v1")
app.include_router(operations_router, prefix="/api/v1")


@app.exception_handler(StarletteHTTPException)
async def http_exception_handler(_, exc: StarletteHTTPException) -> JSONResponse:
    """Chuẩn hóa thông báo lỗi HTTP, giữ nguyên mã lỗi nghiệp vụ nếu có."""
    detail = exc.detail if isinstance(exc.detail, dict) else {"code": "ERR-HTTP", "message": str(exc.detail)}
    return JSONResponse(status_code=exc.status_code, content={"detail": detail})


@app.get("/health", tags=["System"])
async def health_check() -> dict[str, str]:
    return {"status": "ok", "environment": settings.environment}
