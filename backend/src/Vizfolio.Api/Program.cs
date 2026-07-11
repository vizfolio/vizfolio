using FastEndpoints;
using FastEndpoints.Swagger;
using Microsoft.EntityFrameworkCore;
using Vizfolio.Application;
using Vizfolio.Infrastructure;
using Vizfolio.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration);

builder.Services
    .AddFastEndpoints()
    .SwaggerDocument();

var app = builder.Build();

if (app.Configuration.GetValue("Database:AutoMigrate", false))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.UseFastEndpoints(c => c.Endpoints.RoutePrefix = "api");

if (app.Environment.IsDevelopment())
{
    app.UseSwaggerGen();
    app.UseSwaggerUi(c =>
    {
        c.Path = "/swagger";
        c.DocumentTitle = "Vizfolio API";
    });
}

app.Run();

public partial class Program;
