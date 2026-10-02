namespace DataSource.Application.Abstractions;

/// <summary>
/// Application-layer DTO representing a user record fetched from an external data source.
/// Maps from ExternalUser (Infrastructure entity) during multi-source aggregation.
/// </summary>
public record UserData(Guid Id, string Name, string Email);