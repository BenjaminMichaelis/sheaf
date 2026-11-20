using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Sheaf.Data.Security;

public static class MembershipSecurityDbContextOptionsBuilderExtensions
{
    private static readonly MembershipSecurityStampInterceptor Interceptor = new();

    /// <summary>Rotates security stamps on membership removal or role change and rejects bulk writes that bypass it.</summary>
    public static DbContextOptionsBuilder UseMembershipSecurity(this DbContextOptionsBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (builder.Options.FindExtension<CoreOptionsExtension>()?.Interceptors?
                .OfType<MembershipSecurityStampInterceptor>().Any() != true)
        {
            builder.AddInterceptors(Interceptor);
        }

        return builder;
    }
}
