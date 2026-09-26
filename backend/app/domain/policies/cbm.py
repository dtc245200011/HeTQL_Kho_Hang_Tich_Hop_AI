"""Quy tắc tính thể tích riêng cho ngành nội thất."""
from decimal import Decimal, ROUND_HALF_UP


def calculate_cbm(length_mm: int, width_mm: int, height_mm: int) -> Decimal:
    """Tính CBM, từ chối mọi kích thước không dương (ERR-CBM-001)."""
    if min(length_mm, width_mm, height_mm) <= 0:
        raise ValueError("ERR-CBM-001: Dài, Rộng, Cao phải lớn hơn 0.")
    result = Decimal(length_mm * width_mm * height_mm) / Decimal("1000000000")
    return result.quantize(Decimal("0.000001"), rounding=ROUND_HALF_UP)
