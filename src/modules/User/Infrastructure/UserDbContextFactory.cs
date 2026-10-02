using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace User.Infrastructure;

public class UserDbContextFactory : IDesignTimeDbContextFactory<UserDbContext>
{
    public UserDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<UserDbContext>()
            .UseSqlServer("Data Source=localhost;Integrated Security=SSPI;TrustServerCertificate=true;app=LINQPad")
            .Options;

        return new UserDbContext(options);
    }
}