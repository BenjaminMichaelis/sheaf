using Microsoft.EntityFrameworkCore;

namespace Sheaf.Data;

internal static class DomainModelConfiguration
{
    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<ChurchMembership>(entity =>
        {
            entity.HasOne<Church>()
                .WithMany()
                .HasForeignKey(membership => membership.ChurchId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(membership => membership.UserId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
        });

        builder.Entity<Context>(entity =>
        {
            // JSON ownership needs the complete owner key before the finalizing tenancy convention runs.
            entity.HasKey(context => new { context.ChurchId, context.Id });
            entity.HasOne<Church>()
                .WithMany()
                .HasForeignKey(context => context.ChurchId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
            entity.OwnsOne(context => context.CustomFieldSchema, schema =>
            {
                schema.ToJson();
                schema.OwnsMany(fields => fields.Fields);
            });
        });

        builder.Entity<Person>(entity =>
        {
            entity.HasOne<Church>()
                .WithMany()
                .HasForeignKey(person => person.ChurchId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
            entity.HasOne<Person>()
                .WithMany()
                .HasForeignKey(person => person.GuardianPersonId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
        });

        builder.Entity<Capture>()
            .HasOne<Church>()
            .WithMany()
            .HasForeignKey(capture => capture.ChurchId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.Entity<Touchpoint>(entity =>
        {
            entity.HasOne<Church>()
                .WithMany()
                .HasForeignKey(touchpoint => touchpoint.ChurchId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
            entity.HasOne<Person>()
                .WithMany()
                .HasForeignKey(touchpoint => touchpoint.PersonId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
            entity.HasOne<ChurchMembership>()
                .WithMany()
                .HasForeignKey(touchpoint => touchpoint.AuthorLeaderId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
            entity.HasOne<Context>()
                .WithMany()
                .HasForeignKey(touchpoint => new { touchpoint.ChurchId, touchpoint.ContextId })
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
        });

        builder.Entity<Proposal>()
            .HasOne<Church>()
            .WithMany()
            .HasForeignKey(proposal => proposal.ChurchId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.Entity<AuditEntry>()
            .HasOne<Church>()
            .WithMany()
            .HasForeignKey(entry => entry.ChurchId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.Entity<LeaderToken>(entity =>
        {
            entity.HasOne<Church>()
                .WithMany()
                .HasForeignKey(token => token.ChurchId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
            entity.HasOne<ChurchMembership>()
                .WithMany()
                .HasForeignKey(token => token.LeaderId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
        });

        builder.Entity<Relationship>(entity =>
        {
            entity.HasOne<Church>()
                .WithMany()
                .HasForeignKey(relationship => relationship.ChurchId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
            entity.HasOne<Person>()
                .WithMany()
                .HasForeignKey(relationship => relationship.PersonId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
            entity.HasOne<ChurchMembership>()
                .WithMany()
                .HasForeignKey(relationship => relationship.LeaderId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
        });

        builder.Entity<ContextLeader>(entity =>
        {
            entity.HasOne<Church>()
                .WithMany()
                .HasForeignKey(leader => leader.ChurchId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
            entity.HasOne<Context>()
                .WithMany()
                .HasForeignKey(leader => new { leader.ChurchId, leader.ContextId })
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
            entity.HasOne<ChurchMembership>()
                .WithMany()
                .HasForeignKey(leader => leader.LeaderId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
        });

        builder.Entity<ContextMember>(entity =>
        {
            entity.HasOne<Church>()
                .WithMany()
                .HasForeignKey(member => member.ChurchId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
            entity.HasOne<Context>()
                .WithMany()
                .HasForeignKey(member => new { member.ChurchId, member.ContextId })
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
            entity.HasOne<Person>()
                .WithMany()
                .HasForeignKey(member => member.PersonId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
        });

        builder.Entity<Attendance>(entity =>
        {
            entity.HasOne<Church>()
                .WithMany()
                .HasForeignKey(attendance => attendance.ChurchId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
            entity.HasOne<Context>()
                .WithMany()
                .HasForeignKey(attendance => new { attendance.ChurchId, attendance.ContextId })
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
            entity.HasOne<Person>()
                .WithMany()
                .HasForeignKey(attendance => attendance.PersonId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
            entity.HasOne<ChurchMembership>()
                .WithMany()
                .HasForeignKey(attendance => attendance.RecordedByLeaderId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
        });

        builder.Entity<PersonExternalRef>(entity =>
        {
            entity.HasOne<Church>()
                .WithMany()
                .HasForeignKey(reference => reference.ChurchId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
            entity.HasOne<Person>()
                .WithMany()
                .HasForeignKey(reference => reference.PersonId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
        });
    }
}
