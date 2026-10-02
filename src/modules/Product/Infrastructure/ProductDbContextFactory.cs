using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Product.Infrastructure;

public class ProductDbContextFactory : IDesignTimeDbContextFactory<ProductDbContext>
{
    public ProductDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ProductDbContext>()
            .UseSqlServer("Data Source=localhost;Integrated Security=SSPI;TrustServerCertificate=true;app=LINQPad")
            .Options;

        return new ProductDbContext(options);
    }
}