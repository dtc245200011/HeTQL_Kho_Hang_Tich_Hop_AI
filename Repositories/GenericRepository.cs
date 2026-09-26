using DuAnCode.Web.Data;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace DuAnCode.Web.Repositories
{
    public class GenericRepository<T> : IGenericRepository<T> where T : class
    {
        protected readonly ApplicationDbContext _db;
        protected readonly DbSet<T> _set;
        public GenericRepository(ApplicationDbContext db)
        {
            _db = db;
            _set = db.Set<T>();
        }

        public async Task AddAsync(T entity) => await _set.AddAsync(entity);

        public async Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate)
        {
            return await _set.Where(predicate).ToListAsync();
        }

        public async Task<IEnumerable<T>> GetAllAsync()
        {
            return await _set.ToListAsync();
        }

        public async Task<T?> GetByIdAsync(params object[] keyValues)
        {
            return await _set.FindAsync(keyValues);
        }

        public void Remove(T entity) => _set.Remove(entity);

        public void Update(T entity) => _set.Update(entity);

        public async Task<int> SaveChangesAsync() => await _db.SaveChangesAsync();
    }
}
