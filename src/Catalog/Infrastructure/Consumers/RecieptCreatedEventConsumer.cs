using Catalog.Infrastructure.IntegrationEvents;
using MassTransit.RabbitMqTransport;

namespace Catalog.Infrastructure.Consumers;

public class RecieptCreatedEventConsumer(
    CatalogDbContext dbContext,
    IPublishEndpoint publisher,
    ILogger<RecieptCreatedEventConsumer> logger) : IConsumer<RecieptCreatedEvent>
{
    private readonly CatalogDbContext _dbContext = dbContext;
    private readonly IPublishEndpoint _publisher = publisher;
    private readonly ILogger<RecieptCreatedEventConsumer> _logger = logger;

    public async Task Consume(ConsumeContext<RecieptCreatedEvent> context)
    {
        foreach (var item in context.Message.RecieptItems)
        {
            var catalogItem = _dbContext.CatalogItems.FirstOrDefault(c => c.Slug == item.Slug);
            if (catalogItem is null)
            {
                _logger.LogWarning("Catalog Item with slug {var} not found", item.Slug);
                return;
            }

            catalogItem.AddStock(item.Stock);
        }

        await _dbContext.SaveChangesAsync(context.CancellationToken);

        await _publisher.Publish(new CatalogItemStockAvailableEvent
        {
            Slugs = [..context.Message.RecieptItems.Select(r => r.Slug)]
        });
    }
}
