using RonatIa.Games.Api.Startup;
using RonatIa.Games.Application;
using RonatIa.Games.Infrastructure;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// O Render injeta a porta na variável PORT.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"));

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddApi(builder.Configuration);

var app = builder.Build();

app.UseApi();

app.Run();

// Necessário para os testes de integração (WebApplicationFactory<Program>).
public partial class Program;
