using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModsDude.Server.Persistence.Accounts;

namespace ModsDude.Server.Persistence.EntityTypeConfigurations;

/// <summary>
/// The tables ASP.NET Core Identity's user store reads and writes, mapped here because the context
/// cannot derive from Identity's own: that one claims the name <c>Users</c> for itself.
/// </summary>
internal class UserAccountEntityTypeConfiguration : IEntityTypeConfiguration<UserAccount>
{
    public void Configure(EntityTypeBuilder<UserAccount> builder)
    {
        builder.ToTable("UserAccounts");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ConcurrencyStamp).IsConcurrencyToken();
        builder.Property(x => x.UserName).HasMaxLength(256);
        builder.Property(x => x.NormalizedUserName).HasMaxLength(256);
        builder.Property(x => x.Email).HasMaxLength(256);
        builder.Property(x => x.NormalizedEmail).HasMaxLength(256);

        builder.HasIndex(x => x.NormalizedUserName).HasDatabaseName("UserNameIndex").IsUnique();

        // Identity checks the address is free before it saves; this is what stops two sign-ups racing
        // past that check with the same one.
        builder.HasIndex(x => x.NormalizedEmail).HasDatabaseName(UserAccountConstraints.EmailIndex).IsUnique();

        builder.HasIndex(x => x.SignUpRequestId).HasDatabaseName(UserAccountConstraints.SignUpRequestIndex).IsUnique();

        builder.HasMany<IdentityUserClaim<string>>().WithOne().HasForeignKey(x => x.UserId).IsRequired();
        builder.HasMany<IdentityUserLogin<string>>().WithOne().HasForeignKey(x => x.UserId).IsRequired();
        builder.HasMany<IdentityUserToken<string>>().WithOne().HasForeignKey(x => x.UserId).IsRequired();
    }
}

internal class UserAccountClaimEntityTypeConfiguration : IEntityTypeConfiguration<IdentityUserClaim<string>>
{
    public void Configure(EntityTypeBuilder<IdentityUserClaim<string>> builder)
    {
        builder.ToTable("UserAccountClaims");
        builder.HasKey(x => x.Id);
    }
}

internal class UserAccountLoginEntityTypeConfiguration : IEntityTypeConfiguration<IdentityUserLogin<string>>
{
    public void Configure(EntityTypeBuilder<IdentityUserLogin<string>> builder)
    {
        builder.ToTable("UserAccountLogins");
        builder.HasKey(x => new { x.LoginProvider, x.ProviderKey });
    }
}

internal class UserAccountTokenEntityTypeConfiguration : IEntityTypeConfiguration<IdentityUserToken<string>>
{
    public void Configure(EntityTypeBuilder<IdentityUserToken<string>> builder)
    {
        builder.ToTable("UserAccountTokens");
        builder.HasKey(x => new { x.UserId, x.LoginProvider, x.Name });
    }
}

public static class UserAccountConstraints
{
    /// <summary>Violated by a second account with the same address.</summary>
    public const string EmailIndex = "EmailIndex";

    /// <summary>Violated by a resubmit of a sign-up that has already been saved.</summary>
    public const string SignUpRequestIndex = "IX_UserAccounts_SignUpRequestId";

    /// <summary>
    /// Holds every account to its user. Written by hand in the migration, because the account's string
    /// key and the user's typed one cannot be related in the model.
    /// </summary>
    public const string UserForeignKey = "FK_UserAccounts_Users_Id";
}
