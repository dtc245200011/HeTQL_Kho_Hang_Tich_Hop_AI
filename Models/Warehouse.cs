namespace DuAnCode.Web.Models
{
    public class Warehouse
    {
        // Legacy/persisted schema (kept for compatibility with existing DB and other entities)
        public string WarehouseId { get; set; } = null!; // acts as code
        public string WarehouseName { get; set; } = null!;
        public string WarehouseType { get; set; } = "MAIN";
        public decimal MaxCapacityCbm { get; set; }
        public string? Location { get; set; }
        public bool IsActive { get; set; } = true;

        // New standardized aliases for WMS domain (not mapped separately)
        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public string Id { get => WarehouseId; set => WarehouseId = value; }

        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public string Name { get => WarehouseName; set => WarehouseName = value; }

        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public string Code { get => WarehouseId; set => WarehouseId = value; }

        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public string Type { get => WarehouseType; set => WarehouseType = value; }

        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public decimal Capacity { get => MaxCapacityCbm; set => MaxCapacityCbm = value; }
    }
}
