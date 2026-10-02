using DataSource.Domain.Enums;

namespace DataSource.Domain.Entities;

public class DataSource
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Name { get; private set; }
    public ProviderType Provider { get; private set; }

    /// <summary>
    /// Connection string for the external data source.
    /// In production, encrypt this value using ASP.NET Core Data Protection or similar.
    /// </summary>
    public string ConnectionString { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private DataSource() { }

    public DataSource(Guid id, Guid userId, string name, ProviderType provider, string connectionString)
    {
        Id = id;
        UserId = userId;
        Name = name;
        Provider = provider;
        ConnectionString = connectionString;
        CreatedAt = DateTime.UtcNow;
    }
}