"""Đọc cấu hình ứng dụng từ biến môi trường."""
from functools import lru_cache

from pydantic import Field
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    """Cấu hình tập trung, không hard-code thông tin bí mật."""

    model_config = SettingsConfigDict(
        env_file=".env", env_file_encoding="utf-8", env_prefix="WMS_", extra="ignore"
    )

    app_name: str = "Furniture WMS API"
    environment: str = "development"
    debug: bool = False
    database_url: str = Field(
        default="mssql+aioodbc://sa:YourStrongPassword@localhost:1433/FurnitureWMS?driver=ODBC+Driver+18+for+SQL+Server&TrustServerCertificate=yes"
    )
    # Giá trị này chỉ phục vụ khởi động local; production phải set WMS_JWT_SECRET_KEY.
    jwt_secret_key: str = Field(
        default="development-only-secret-must-be-replaced-2026",
        min_length=32,
    )
    jwt_algorithm: str = "HS256"
    access_token_expire_minutes: int = Field(default=60, ge=1, le=60)
    lock_timeout_seconds: int = Field(default=8, ge=5, le=10)


@lru_cache
def get_settings() -> Settings:
    """Trả về một cấu hình duy nhất trong vòng đời tiến trình."""
    return Settings()
