namespace DuAnCode.Web.Services
{
    public interface IAiService
    {
        Task<string> SuggestReplenishmentAsync(string skuId);
        Task<IEnumerable<dynamic>> GetAllSkusAsync();
        Task<IEnumerable<dynamic>> GetAllLedgersAsync();
    }
}
