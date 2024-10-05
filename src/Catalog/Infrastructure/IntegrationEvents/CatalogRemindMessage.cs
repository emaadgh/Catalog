namespace Catalog.Infrastructure.IntegrationEvents;

public sealed record CatalogRemindMessage(
    Guid UserId,
    string slug,
    string Message,
    NotifyChannel NotifyChannel);


public enum NotifyChannel
{
    SMS = 1,
    Email = 2,
    MSTeams = 3,
    Telegram = 4
}