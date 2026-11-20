namespace ModsDude.Server.Api.Accounts.Email;

public sealed record EmailOptions
{
    public const string SectionName = "Email";


    public EmailDelivery Delivery { get; init; } = EmailDelivery.Resend;

    public string From { get; init; } = "ModsDude <no-reply@modsdude.com>";

    /// <summary>From user secrets or the environment, never from appsettings.</summary>
    public string ResendApiKey { get; init; } = "";

    /// <summary>Where <see cref="EmailDelivery.PickupFolder"/> writes emails. The temp folder when empty.</summary>
    public string PickupFolder { get; init; } = "";
}

public enum EmailDelivery
{
    Resend,

    /// <summary>Writes each email to a file instead of sending it, for development.</summary>
    PickupFolder
}
