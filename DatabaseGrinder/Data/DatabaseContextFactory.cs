using DatabaseGrinder.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DatabaseGrinder.Data;

/// <summary>
/// Design-time factory for DatabaseContext to support EF Core migrations
/// </summary>
public class DatabaseContextFactory : IDesignTimeDbContextFactory<DatabaseContext>
{
    public DatabaseContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<DatabaseContext>();
        
        // Use a default connection string for design-time operations
        // This will be overridden at runtime by the actual configuration
        optionsBuilder.UseNpgsql("Host=localhost;Database=postgres;Username=postgres;Password=postgres;");

        var context = new DatabaseContext(optionsBuilder.Options);
        
        // Set default schema name for design-time
        context.SetSchemaName("databasegrinder");
        
        return context;
    }
}