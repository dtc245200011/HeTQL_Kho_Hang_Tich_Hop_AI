"""Mã lỗi nghiệp vụ và biểu diễn lỗi API thống nhất."""
from fastapi import HTTPException, status


class BusinessError(HTTPException):
    def __init__(self, status_code: int, code: str, detail: str) -> None:
        super().__init__(status_code=status_code, detail={"code": code, "message": detail})


def forbidden(detail: str = "Bạn không có quyền thực hiện thao tác này.") -> BusinessError:
    return BusinessError(status.HTTP_403_FORBIDDEN, "ERR-AUTH-403", detail)


def unauthorized(detail: str = "Thông tin xác thực không hợp lệ.") -> BusinessError:
    return BusinessError(status.HTTP_401_UNAUTHORIZED, "ERR-AUTH-401", detail)
