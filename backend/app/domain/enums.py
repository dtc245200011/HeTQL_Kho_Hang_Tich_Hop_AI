"""Các enum khớp với CHECK constraint trong SQL Server."""
from enum import StrEnum


class StockStatus(StrEnum):
    GOOD = "GOOD"
    DAMAGED = "DAMAGED"


class DocumentStatus(StrEnum):
    DRAFT = "DRAFT"
    PENDING_L1 = "PENDING_L1"
    PENDING_L2 = "PENDING_L2"
    PENDING_L3 = "PENDING_L3"
    APPROVED = "APPROVED"
    CANCELLED = "CANCELLED"


class RoleId(StrEnum):
    ADMIN = "ADMIN"
    WAREHOUSE_KEEPER = "WAREHOUSE_KEEPER"
    ACCOUNTANT = "ACCOUNTANT"
    WAREHOUSE_MANAGER = "WAREHOUSE_MANAGER"
    DIRECTOR = "DIRECTOR"
    QC = "QC"
    STAFF = "STAFF"
