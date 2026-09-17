using System.Linq.Expressions;
using ITBees.Interfaces.Repository;
using Microsoft.EntityFrameworkCore;

namespace ITBees.Printers.DevHost;

// The ITBees generic repositories over the sandbox database. Like the real MySQL ones, every
// repository instance works on a DbContext of its own.

public abstract class DevRepositoryBase<T> : IDisposable where T : class
{
    protected readonly DevHostContext Context;

    protected DevRepositoryBase(IDbContextFactory<DevHostContext> contextFactory)
    {
        Context = contextFactory.CreateDbContext();
    }

    protected IQueryable<T> Query(Expression<Func<T, bool>>? predicate,
        params Expression<Func<T, object>>[] includeProperties)
    {
        IQueryable<T> query = Context.Set<T>();
        foreach (var includeProperty in includeProperties)
        {
            query = query.Include(includeProperty);
        }

        return predicate == null ? query : query.Where(predicate);
    }

    public void Dispose()
    {
        Context.Dispose();
    }
}

public class DevReadOnlyRepository<T> : DevRepositoryBase<T>, IReadOnlyRepository<T> where T : class
{
    public DevReadOnlyRepository(IDbContextFactory<DevHostContext> contextFactory) : base(contextFactory)
    {
    }

    public bool HasData(Expression<Func<T, bool>> predicate) => Query(predicate).Any();

    public T GetFirst(Expression<Func<T, bool>> predicate, params Expression<Func<T, object>>[] includeProperties) =>
        Query(predicate, includeProperties).First();

    public ICollection<T> GetData(Expression<Func<T, bool>> predicate,
        params Expression<Func<T, object>>[] includeProperties) => Query(predicate, includeProperties).ToList();

    public int GetDataCount(Expression<Func<T, bool>> predicate) => Query(predicate).Count();

    public IQueryable<T> GetDataQueryable(Expression<Func<T, bool>> predicate) => Query(predicate);

    public IQueryable<T> GetDataQueryable(Expression<Func<T, bool>> predicate,
        params Expression<Func<T, object>>[] includeProperties) => Query(predicate, includeProperties);

    public PaginatedResult<T> GetDataPaginated(Expression<Func<T, bool>> predicate, int page, int elementsPerPage,
        string sortColumn, SortOrder sortOrder, params Expression<Func<T, object>>[] includeProperties) =>
        throw new NotSupportedException("The sandbox repositories do not paginate.");

    public PaginatedResult<T> GetDataPaginated(Expression<Func<T, bool>> predicate, SortOptions sortOptions,
        params Expression<Func<T, object>>[] includeProperties) =>
        throw new NotSupportedException("The sandbox repositories do not paginate.");

    public ICollection<T> GetDataFromStoredProcedure(string procedureName, params object[] procedureArgument) =>
        throw new NotSupportedException();

    public ICollection<T2> Sql<T2>(string sql) where T2 : class => throw new NotSupportedException();
}

public class DevWriteOnlyRepository<T> : DevRepositoryBase<T>, IWriteOnlyRepository<T> where T : class
{
    public DevWriteOnlyRepository(IDbContextFactory<DevHostContext> contextFactory) : base(contextFactory)
    {
    }

    public T InsertData(T entity)
    {
        Context.Add(entity);
        Context.SaveChanges();
        return entity;
    }

    public ICollection<T> InsertData(ICollection<T> entities)
    {
        Context.AddRange(entities);
        Context.SaveChanges();
        return entities;
    }

    public ICollection<T> UpdateData(Expression<Func<T, bool>> predicate, Action<T> updateAction,
        params Expression<Func<T, object>>[] includeProperties)
    {
        var entities = Query(predicate, includeProperties).ToList();
        foreach (var entity in entities)
        {
            updateAction(entity);
        }

        Context.SaveChanges();
        return entities;
    }

    public int DeleteData(Expression<Func<T, bool>> predicate)
    {
        var entities = Query(predicate).ToList();
        Context.RemoveRange(entities);
        Context.SaveChanges();
        return entities.Count;
    }

    public void DeleteData(Expression<Func<T, bool>> predicate, params Expression<Func<T, object>>[] includeProperties)
    {
        Context.RemoveRange(Query(predicate, includeProperties).ToList());
        Context.SaveChanges();
    }

    public void Sql(string sql) => throw new NotSupportedException();
}
