using Sheaf.Data.Tenancy;

namespace Sheaf.Data;

public sealed class TenantContext : ICurrentChurch
{
    public Guid? ChurchId { get; private set; }

    public Guid? LeaderId { get; private set; }

    internal sealed class Initializer(TenantContext currentChurch, ApplicationDbContext db)
        : ITenantContextInitializer
    {
        public void Initialize(Guid? churchId, Guid? leaderId)
        {
            if (churchId == Guid.Empty || leaderId == Guid.Empty || (churchId is null && leaderId is not null))
            {
                throw new ArgumentException("Church selection requires non-empty IDs and a church for its membership.");
            }

            if (currentChurch.ChurchId != churchId && db.ChangeTracker.Entries<ITenanted>().Any())
            {
                throw new InvalidOperationException("Cannot change churches while tenanted entities are tracked.");
            }

            currentChurch.ChurchId = churchId;
            currentChurch.LeaderId = leaderId;
        }
    }
}
