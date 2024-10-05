using Catalog.Infrastructure.ExternalService;
using Catalog.Infrastructure.IntegrationEvents;
using Microsoft.AspNetCore.Mvc;

namespace Catalog.Endpoints;

public static class CatalogItemEndpoints
{
    public static IEndpointRouteBuilder MapCatalogItemEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/", CreateItem);
        app.MapPut("/", UpdateItem);
        app.MapPatch("/max_stock_threshold", UpdateMaxStockThreshold);
        app.MapDelete("/{slug:required}", DeleteItemById);
        app.MapGet("/{slug:required}", GetItemById);
        app.MapGet("/", GetItems);

        app.MapPost("/{slug:required}/reminder", CreateRemindItem);

        return app;
    }

    public static async Task<Results<Ok, BadRequest<string>, NotFound<string>>> CreateRemindItem(
        [FromQuery(Name = "user_id")] Guid userId,
        [FromRoute] string slug,
        [AsParameters] CatalogServices services,
        IPublishEndpoint publishEndpoint,
        QuickLinkerService quickLinkerService,
        CancellationToken cancellationToken)
    {
        var Item = await services.Context.CatalogItems
                                                .Include(c => c.Reminds)
                                                .FirstOrDefaultAsync(i => i.Slug == slug, cancellationToken);
        if (Item is null)
        {
            return TypedResults.NotFound($"Item with slug {slug} not found.");
        }

        if (Item.AvailableStock > 0)
        {
            return TypedResults.BadRequest($"Item with slug {slug} is already available");
        }

        if (Item.Reminds.Any(r => r.UserId == userId))
        {
            return TypedResults.BadRequest($"Item with slug {slug} is already has a reminder for this user");
        }

        string longURL = $"http://localhost:5074/api/v1/items/{slug}";
        string shortURL = await quickLinkerService.GetShortURL(longURL);

        var message = $"""
            Hi, 
            The Item {Item.Name} you requested is available Now!
            Click on the link below to continue
            {shortURL}
            """;

        var remindMessage = new CatalogRemindMessage(userId, slug, message, NotifyChannel.Email);

        await publishEndpoint.Publish(remindMessage, cancellationToken);

        Item.AddRemind(userId, DateTime.UtcNow);
        await services.Context.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok();
    }

    public static async Task<Results<Created, ValidationProblem, BadRequest<string>>> CreateItem(
        [AsParameters] CatalogServices services,
        CreateCatalogItemRequest itemToCreate,
        IValidator<CreateCatalogItemRequest> validator,
        IPublishEndpoint publishEndpoint,
        CancellationToken cancellationToken)
    {
        var validate = validator.Validate(itemToCreate);
        if (!validate.IsValid)
        {
            return TypedResults.ValidationProblem(validate.ToDictionary());
        }

        var hasCategory = await services.Context.CatalogCategories.AnyAsync(x => x.Id == itemToCreate.CatalogId, cancellationToken);
        if (!hasCategory)
        {
            return TypedResults.BadRequest($"A category Id is not valid.");
        }

        var hasBrand = await services.Context.CatalogBrands.AnyAsync(x => x.Id == itemToCreate.BrandId, cancellationToken);
        if (!hasBrand)
        {
            return TypedResults.BadRequest($"A brand Id is not valid.");
        }

        var hasItemSlug = await services.Context.CatalogItems.AnyAsync(x => x.Slug == itemToCreate.Name.ToKebabCase(), cancellationToken);
        if (hasItemSlug)
        {
            return TypedResults.BadRequest($"A Item with the slug '{itemToCreate.Name.ToKebabCase()}' already exists.");
        }

        var item = CatalogItem.Create(
            itemToCreate.Name,
            itemToCreate.Description,
            itemToCreate.MaxStockThreshold,
            itemToCreate.BrandId, itemToCreate.CatalogId);

        services.Context.CatalogItems.Add(item);
        await services.Context.SaveChangesAsync(cancellationToken);

        var detailUrl = $"/catalog/api/v1/items/{item.Slug}";
        var loadedItem = await services.Context.CatalogItems
                                                    .Include(ci => ci.CatalogBrand)
                                                    .Include(ci => ci.CatalogCategory)
                                                    .FirstAsync(x => x.Slug == item.Slug);

        await services.Publish.Publish(new CatalogItemAddedEvent(
                loadedItem.Name,
                loadedItem.Description,
                loadedItem.CatalogCategory.Category,
                loadedItem.CatalogBrand.Brand,
                loadedItem.Slug,
                detailUrl));

        return TypedResults.Created(detailUrl);
    }

    public static async Task<Results<Created, ValidationProblem, NotFound<string>, BadRequest<string>>> UpdateItem(
    [AsParameters] CatalogServices services,
    UpdateCatalogItemRequest itemToUpdate,
    IValidator<UpdateCatalogItemRequest> validator,
    CancellationToken cancellationToken)
    {
        var validate = validator.Validate(itemToUpdate);
        if (!validate.IsValid)
        {
            return TypedResults.ValidationProblem(validate.ToDictionary());
        }

        var Item = await services.Context.CatalogItems
                                                .FirstOrDefaultAsync(i => i.Slug == itemToUpdate.slug, cancellationToken);
        if (Item is null)
        {
            return TypedResults.NotFound($"Item with slug {itemToUpdate.slug} not found.");
        }

        var hasCategory = await services.Context.CatalogCategories.AnyAsync(x => x.Id == itemToUpdate.CatalogId, cancellationToken);
        if (!hasCategory)
        {
            return TypedResults.BadRequest($"A category Id is not valid.");
        }

        var hasBrand = await services.Context.CatalogBrands.AnyAsync(x => x.Id == itemToUpdate.BrandId, cancellationToken);
        if (!hasBrand)
        {
            return TypedResults.BadRequest($"A brand Id is not valid.");
        }

        Item.Update(itemToUpdate.Description,
                    itemToUpdate.BrandId,
                    itemToUpdate.CatalogId);

        await services.Context.SaveChangesAsync(cancellationToken);

        var loadedItem = await services.Context.CatalogItems
                                            .Include(ci => ci.CatalogBrand)
                                            .Include(ci => ci.CatalogCategory)
                                            .FirstAsync(x => x.Slug == Item.Slug);

        var detailUrl = $"/catalog/api/v1/items/{loadedItem.Slug}";

        await services.Publish.Publish(new CatalogItemChangedEvent(
                loadedItem.Name,
                loadedItem.Description,
                loadedItem.CatalogCategory.Category,
                loadedItem.CatalogBrand.Brand,
                loadedItem.Slug,
                detailUrl));

        return TypedResults.Created(detailUrl);
    }

    public static async Task<Results<Created, ValidationProblem, NotFound<string>, BadRequest<string>>> UpdateMaxStockThreshold(
    [AsParameters] CatalogServices services,
    UpdateCatalogItemMaxStockThresholdRequest itemToUpdate,
    IValidator<UpdateCatalogItemMaxStockThresholdRequest> validator,
    CancellationToken cancellationToken)
    {
        var validate = validator.Validate(itemToUpdate);
        if (!validate.IsValid)
        {
            return TypedResults.ValidationProblem(validate.ToDictionary());
        }

        var Item = await services.Context.CatalogItems.FirstOrDefaultAsync(i => i.Slug == itemToUpdate.Slug, cancellationToken);
        if (Item is null)
        {
            return TypedResults.NotFound($"Item with Slug {itemToUpdate.Slug} not found.");
        }

        Item.SetMaxStockThreshold(itemToUpdate.MaxStockThreshold);

        await services.Context.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/catalog/api/v1/items/{Item.Slug}");
    }

    public static async Task<Results<NoContent, NotFound, BadRequest<string>>> DeleteItemById(
    [AsParameters] CatalogServices services,
    string slug,
    CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(slug))
        {
            return TypedResults.BadRequest("Slug is not valid.");
        }

        var item = await services.Context.CatalogItems.FirstOrDefaultAsync(x => x.Slug == slug);
        if (item is null)
        {
            return TypedResults.NotFound();
        }

        services.Context.CatalogItems.Remove(item);
        await services.Context.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    public static async Task<Results<Ok<CatalogItemResponse>, NotFound, BadRequest<string>>> GetItemById(
    [AsParameters] CatalogServices services,
    string slug)
    {
        if (string.IsNullOrEmpty(slug))
        {
            return TypedResults.BadRequest("Slug is not valid.");
        }

        var item = await services.Context.CatalogItems
                                         .Include(x => x.CatalogBrand)
                                         .Include(x => x.CatalogCategory)
                                         .Include(x => x.Medias)
                                         .FirstOrDefaultAsync(ci => ci.Slug == slug);
        if (item is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(
            new CatalogItemResponse(
                item.Name,
                item.Slug,
                item.Description,
                item.CatalogBrandId,
                item.CatalogBrand.Brand,
                item.CatalogCategoryId,
                item.CatalogCategory.Category,
                item.Price,
                item.AvailableStock,
                item.MaxStockThreshold, [.. item.Medias]));
    }

    public static async Task<Results<Ok<IEnumerable<CatalogItemResponse>>, BadRequest<string>>> GetItems(
    [AsParameters] CatalogServices services,
    CancellationToken cancellationToken)
    {
        var items = (await services.Context.CatalogItems
                                          .Include(x => x.CatalogBrand)
                                          .Include(x => x.CatalogCategory)
                                          .OrderBy(c => c.Name)
                                          .ToListAsync(cancellationToken))
                                          .Select(x => new CatalogItemResponse(x.Name,
                                                                               x.Slug,
                                                                               x.Description,
                                                                               x.CatalogBrandId,
                                                                               x.CatalogBrand.Brand,
                                                                               x.CatalogCategoryId,
                                                                               x.CatalogCategory.Category,
                                                                               x.Price,
                                                                               x.AvailableStock,
                                                                               x.MaxStockThreshold, [.. x.Medias]))
                                          ;

        return TypedResults.Ok<IEnumerable<CatalogItemResponse>>(items);
    }
}
