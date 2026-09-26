"""SQLAlchemy declarative base và kiểu thời gian thống nhất."""
from datetime import datetime, timezone

from sqlalchemy import DateTime, func
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column


class Base(DeclarativeBase):
    pass


class CreatedAtMixin:
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=False), server_default=func.sysdatetime(), nullable=False)
