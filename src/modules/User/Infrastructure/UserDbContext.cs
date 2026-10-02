using Microsoft.EntityFrameworkCore;
using User.Domain.Entities;

namespace User.Infrastructure;

public class UserDbContext : DbContext
{
    public UserDbContext(DbContextOptions<UserDbContext> options) : base(options) { }

    public DbSet<User.Domain.Entities.User> Users => Set<User.Domain.Entities.User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<User.Domain.Entities.User>(entity =>
                                                                               {
                                                                                   entity.ToTable("Users");
                                                                                   entity.HasKey(x => x.Id);
                                                                                   entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
                                                                                   entity.Property(x => x.Email).HasMaxLength(256).IsRequired();
                                                                                   entity.Ignore(x => x.DomainEvents);
                                                                               });
}