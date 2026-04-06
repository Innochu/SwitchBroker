using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using StackExchange.Redis;
using SwitchBroker;

var builder = WebApplication.CreateBuilder(args);

// ── Configuration ─────────────────────────────────────────────────────────────
builder.Services.Configure<BrokerOptions>(
    builder.Configuration.GetSection(BrokerOptions.SectionName));

// ── Ticket store: Redis if configured, otherwise in-memory ───────────────────
var redisConn = builder.Configuration["Broker:RedisConnectionString"];
if (!string.IsNullOrWhiteSpace(redisConn))
{
    var redis = ConnectionMultiplexer.Connect(redisConn);
    builder.Services.AddSingleton<IConnectionMultiplexer>(redis);
    builder.Services.AddSingleton<ITicketStore, RedisTicketStore>();
}
else
{
    builder.Services.AddSingleton<ITicketStore, InMemoryTicketStore>();
}

var registryPath = Path.Combine(
    builder.Environment.ContentRootPath, "route-registry.json");
builder.Services.AddSingleton(RouteRegistry.LoadFromFile(registryPath));

// ── Core services ─────────────────────────────────────────────────────────────
builder.Services.AddSingleton<ITicketService, TicketService>();

builder.Services.AddEndpointsApiExplorer();

// ── Swagger with header inputs ────────────────────────────────────────────────
builder.Services.AddSwaggerGen(c =>
{
    // Define the two security schemes
    c.AddSecurityDefinition("BrokerSecret", new OpenApiSecurityScheme
    {
        Name = "X-Broker-Secret",
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Description = "Required on POST /api/switch/tickets (issue endpoint)"
    });

    c.AddSecurityDefinition("AppKey", new OpenApiSecurityScheme
    {
        Name = "X-App-Key",
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Description = "Required on POST /api/switch/tickets/{id}/consume"
    });

    c.AddSecurityDefinition("AppName", new OpenApiSecurityScheme
    {
        Name = "X-App-Name",
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Description = "Required on POST /api/switch/tickets/{id}/consume (value: legacy or modern)"
    });

    // Apply all three globally so Swagger shows the fields on every endpoint
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "BrokerSecret"
                }
            },
            Array.Empty<string>()
        }
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "AppKey"
                }
            },
            Array.Empty<string>()
        }
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "AppName"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// ── Middleware pipeline ───────────────────────────────────────────────────────
if (app.Environment.IsDevelopment() || app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseMiddleware<TrustedCallerMiddleware>();

// ── Endpoints ─────────────────────────────────────────────────────────────────
app.MapPost("/api/switch/tickets", async (
    [FromBody] SwitchRequest request,
    ITicketService tickets,
    ILogger<Program> log,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.TargetApp))
        return Results.BadRequest(new ErrorResponse("targetApp is required."));
    if (string.IsNullOrWhiteSpace(request.RouteKey))
        return Results.BadRequest(new ErrorResponse("routeKey is required."));
    if (string.IsNullOrWhiteSpace(request.UserId))
        return Results.BadRequest(new ErrorResponse("userId is required."));
    if (string.IsNullOrWhiteSpace(request.TenantId))
        return Results.BadRequest(new ErrorResponse("tenantId is required."));

    try
    {
        var response = await tickets.IssueAsync(request, ct);
        return Results.Ok(response);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new ErrorResponse(ex.Message));
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Unhandled error in POST /api/switch/tickets");
        return Results.Problem("An internal error occurred.");
    }
})
.WithName("IssueTicket")
.WithTags("Switch Broker")
.WithSummary("Issue a one-time switch ticket (source app → broker, server-to-server)");

app.MapPost("/api/switch/tickets/{ticketId}/consume", async (
    string ticketId,
    [FromBody] ConsumeRequest body,
    HttpContext ctx,
    ITicketService tickets,
    ILogger<Program> log,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(ticketId))
        return Results.BadRequest(new ErrorResponse("ticketId path segment is required."));
    if (string.IsNullOrWhiteSpace(body.ExpectedTargetApp))
        return Results.BadRequest(new ErrorResponse("expectedTargetApp is required."));

    var verifiedApp = ctx.Items["VerifiedAppName"]?.ToString();
    if (verifiedApp != body.ExpectedTargetApp.ToLowerInvariant())
        return Results.BadRequest(new ErrorResponse(
            "expectedTargetApp does not match the authenticated X-App-Name."));

    try
    {
        var result = await tickets.ConsumeAsync(ticketId, body.ExpectedTargetApp, ct);

        if (result is null)
            return Results.UnprocessableEntity(new ErrorResponse(
                "Ticket invalid",
                "The ticket is missing, expired, already used, or the target app does not match."));

        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Unhandled error in POST /api/switch/tickets/{TicketId}/consume", ticketId);
        return Results.Problem("An internal error occurred.");
    }
})
.WithName("ConsumeTicket")
.WithTags("Switch Broker")
.WithSummary("Consume a switch ticket (target app → broker, server-to-server)");

app.MapGet("/api/switch/healthz", () => Results.Ok(new { status = "ok", utc = DateTimeOffset.UtcNow }))
   .WithName("Health")
   .WithTags("Ops");

app.Run();