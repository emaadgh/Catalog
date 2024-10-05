using Catalog.Infrastructure.ExternalService;
using Catalog.Infrastructure.IntegrationEvents;

var builder = WebApplication.CreateBuilder(args);

builder.AddApplicationServices();
builder.AddLoggerConfigs();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient<QuickLinkerService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapGroup("/api/v1/brands")
   .WithTags("Brand APIs")
   .MapCatalogBrandEndpoints();

app.MapGroup("/api/v1/categories")
   .WithTags("Category APIs")
   .MapCatalogCategoryEndpoints();

app.MapGroup("/api/v1/items")
   .WithTags("Item APIs")
   .MapCatalogItemEndpoints();
 
app.Run();