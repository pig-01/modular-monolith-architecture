using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Order.Infrastructure;

public class OrderDbContextFactory
    : IDesignTimeDbContextFactory<OrderDbContext>,
        IDbContextFactory<OrderDbContext>
{
    public OrderDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseSqlServer("Data Source=localhost;Integrated Security=SSPI;TrustServerCertificate=true;app=LINQPad")
            .Options;

        return new OrderDbContext(options);
    }

    public OrderDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseSqlServer("Data Source=localhost;Integrated Security=SSPI;TrustServerCertificate=true;app=LINQPad")
            .Options;

        return new OrderDbContext(options);
    }
}