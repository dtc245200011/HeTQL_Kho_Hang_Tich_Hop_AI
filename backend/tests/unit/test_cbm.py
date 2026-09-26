from decimal import Decimal

import pytest

from app.domain.policies.cbm import calculate_cbm


def test_calculate_cbm_returns_six_decimal_places() -> None:
    assert calculate_cbm(1200, 600, 800) == Decimal("0.576000")


@pytest.mark.parametrize("dimensions", [(0, 2, 3), (1, -2, 3), (1, 2, 0)])
def test_calculate_cbm_rejects_invalid_dimensions(dimensions: tuple[int, int, int]) -> None:
    with pytest.raises(ValueError, match="ERR-CBM-001"):
        calculate_cbm(*dimensions)
