namespace DataSource.Infrastructure.MultiDb;

/// <summary>
/// A lightweight entity that maps to the shared "Users" table schema.
/// This type is used by ExternalUserDbContext when querying external data sources.
/// All external databases are expected to have a Users table with at least these columns.
/// </summary>
public class ExternalUser
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}