using Sheaf.Data;

namespace Sheaf.Core.Authentication;

/// <summary>A user's church and membership-derived role, without directory access to other users.</summary>
public sealed record ChurchInfo(Guid ChurchId, string ChurchName, ChurchMembershipRole Role);

/// <summary>The selected church membership used by the authentication boundary.</summary>
public sealed record ChurchSelection(Guid ChurchId, Guid MembershipId, ChurchMembershipRole Role);
