using DataSource.Application.Abstractions;
using DataSource.Infrastructure.MultiDb;
using Riok.Mapperly.Abstractions;
using DataSourceEntity = DataSource.Domain.Entities.DataSource;

namespace DataSource.Application.Mapping;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class DataSourceMapper
{
    public static partial DataSourceDto ToDto(DataSourceEntity source);

    public static partial IQueryable<DataSourceDto> ProjectToDto(this IQueryable<DataSourceEntity> source);

    public static partial UserData ToUserData(ExternalUser source);
}