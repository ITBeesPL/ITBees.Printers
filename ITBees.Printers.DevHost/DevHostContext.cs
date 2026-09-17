using ITBees.Models.Users;
using Microsoft.EntityFrameworkCore;

namespace ITBees.Printers.DevHost;

public class DevHostContext : DbContext
{
    public DevHostContext(DbContextOptions<DevHostContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Just enough of the user account for the foreign keys of the printing module.
        modelBuilder.Entity<UserAccount>(account =>
        {
            account.HasKey(x => x.Guid);
            account.Ignore(x => x.LastUsedCompany);
            account.Ignore(x => x.Language);
            account.Ignore(x => x.UserAccountModules);
            account.Ignore(x => x.UsersInCompanies);
        });

        ITBees.Printers.Setup.DbModelBuilder.Register(modelBuilder);
        base.OnModelCreating(modelBuilder);
    }
}
