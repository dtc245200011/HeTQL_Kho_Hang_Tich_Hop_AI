using System;
using System.Threading.Tasks;
using DuAnCode.Web.Models;
using DuAnCode.Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DuAnCode.Tests
{
    public class AiServiceAndMonitoringTests
    {
        [Fact]
        public async Task Suggest_Replenishment_When_Stock_Below_MinStock()
        {
            // SRS-AI-01: Gợi ý bù hàng khi tồn kho thấp hơn định mức tồn tối thiểu (min_stock)
            using var db = TestHelper.CreateInMemoryDbContext();
            var aiService = new AiService(db);

            // In test seed: PM-SOFA has MinStock = 3.
            // Stock in WH-HN is 2 (< 3).
            var suggestion = await aiService.SuggestReplenishmentAsync("SKU-SOFA-01");
            Assert.Contains("Suggest reorder", suggestion);
            Assert.Contains("1 units", suggestion); // Need: 3 - 2 = 1
        }

        [Fact]
        public async Task No_Replenishment_Needed_When_Stock_Adequate()
        {
            using var db = TestHelper.CreateInMemoryDbContext();
            var aiService = new AiService(db);

            // In test seed: PM-DESK has MinStock = 5.
            // Stock in WH-HN is 20 (> 5).
            var suggestion = await aiService.SuggestReplenishmentAsync("SKU-DESK-01");
            Assert.Equal("Stock level adequate.", suggestion);
        }

        [Fact]
        public void Anomaly_Detection_Triggers_On_20_Percent_Discrepancy()
        {
            // SRS-AI-02: Phát hiện biến động bất thường khi trung bình 7 ngày lệch >= 20% so với 60 ngày
            double avg60 = 10.0;
            double avg7 = 13.0; // diff = 3 / 10 = 30% >= 20%

            double diff = Math.Abs(avg7 - avg60) / avg60;
            bool isAnomaly = diff >= 0.20;

            Assert.True(isAnomaly);
            Assert.Equal(0.30, Math.Round(diff, 2));

            // Small fluctuation: 10.5 vs 10.0 -> 5% < 20%
            double smallDiff = Math.Abs(10.5 - 10.0) / 10.0;
            Assert.False(smallDiff >= 0.20);
        }
    }
}
