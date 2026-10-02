using Riok.Mapperly.Abstractions;
using User.Application.Abstractions;
using UserEntity = User.Domain.Entities.User;

namespace User.Application.Mapping;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class UserMapper
{
    public static partial UserDto ToDto(UserEntity source);

    public static partial IQueryable<Queries.UserDto> ProjectToDto(this IQueryable<UserEntity> source);
}