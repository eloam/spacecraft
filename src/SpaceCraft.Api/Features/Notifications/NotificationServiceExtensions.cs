namespace SpaceCraft.Api.Features.Notifications;

public static class NotificationServiceExtensions
{
    /// <summary>
    /// The settings the dispatcher and the retention sweep read, checked when the host
    /// starts. The two hosted services themselves stay listed in Program.cs beside the other
    /// dispatchers, where what runs in this process is decided.
    /// </summary>
    public static IServiceCollection AddSpaceCraftNotifications(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<NotificationOptions>()
            .Bind(configuration.GetSection(NotificationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }
}
