using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DataSource.Infrastructure;

public class DataSourceDbContextFactory : IDesignTimeDbContextFactory<DataSourceDbContext>
{
    public DataSourceDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<DataSourceDbContext>()
            .UseSqlServer("Data Source=localhost;Integrated Security=SSPI;TrustServerCertificate=true")
            .Options;

        return new DataSourceDbContext(options);
    }
}