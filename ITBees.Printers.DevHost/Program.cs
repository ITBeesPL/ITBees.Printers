using ITBees.Interfaces.Repository;
using ITBees.Models.Users;
using ITBees.Printers;
using ITBees.Printers.Controllers;
using ITBees.Printers.DevHost;
using ITBees.Printers.Setup;
using ITBees.UserManager.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

// Development sandbox - see the csproj. API + a test page on :5190, print agents on :5191.
//   dotnet run --project ITBees.Printers.DevHost
//   ITBees.Printers.Agent.exe --site http://localhost:5190
const int apiPort = 5190;
const int agentPort = 5191;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(kestrel => kestrel.ListenLocalhost(apiPort));

builder.Services.AddDbContextFactory<DevHostContext>(options => options.UseInMemoryDatabase("ITBees.Printers.DevHost"));
builder.Services.AddScoped(typeof(IReadOnlyRepository<>), typeof(DevReadOnlyRepository<>));
builder.Services.AddScoped(typeof(IWriteOnlyRepository<>), typeof(DevWriteOnlyRepository<>));
builder.Services.AddScoped<IAspCurrentUserService, DevCurrentUserService>();

builder.Services.AddAuthentication(DevUser.Scheme)
    .AddScheme<AuthenticationSchemeOptions, DevUserAuthenticationHandler>(DevUser.Scheme, null);
builder.Services.AddAuthorization();
builder.Services.AddControllers().AddApplicationPart(typeof(PrintJobController).Assembly);
// A real ITBees admin panel served from another port can use this sandbox as its API.
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

new PrintersSetup().Register(builder.Services, new PrintersSettings
{
    ServiceName = "ITBees.Printers sandbox",
    AgentPort = agentPort,
    DocumentTypes =
    {
        new PrintDocumentType("StockLabel", "Etykiety magazynowe 50 x 30 mm"),
        new PrintDocumentType("Invoice", "Faktury")
    }
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    using var context = scope.ServiceProvider.GetRequiredService<IDbContextFactory<DevHostContext>>().CreateDbContext();
    context.Add(new UserAccount
    {
        Guid = DevUser.Guid, Email = "dev@example.com", FirstName = "Dev", LastName = "User", Phone = string.Empty
    });
    context.SaveChanges();
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// What an ITBees panel's auth guard asks for after the login - the sandbox user, always.
app.MapGet("/MyAccount", () => new
{
    guid = DevUser.Guid,
    email = "dev@example.com",
    firstName = "Dev",
    lastName = "User",
    displayName = "Dev User",
    phone = string.Empty,
    companies = Array.Empty<object>(),
    lastUsedCompanyGuid = (Guid?)null,
    language = "pl"
});

// The connect page the print agent opens in the browser is a client-side route of the test page.
app.MapFallbackToFile("index.html");

app.Run();
