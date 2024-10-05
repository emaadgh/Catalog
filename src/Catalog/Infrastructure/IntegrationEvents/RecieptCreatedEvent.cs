namespace Catalog.Infrastructure.IntegrationEvents;

public class RecieptCreatedEvent
{
    public ICollection<RecieptItem> RecieptItems { get; set; } = null!;
}

public class RecieptItem
{
    public string Slug { get; set; } = null!;
    public int Stock { get; set; }
}