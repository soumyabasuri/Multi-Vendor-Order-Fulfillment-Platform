using System.Text;
using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MultiVendorPlatform.Api.Middleware;
using MultiVendorPlatform.Infrastructure.BackgroundJobs;
using MultiVendorPlatform.Infrastructure.Caching;
using MultiVendorPlatform.Infrastructure.Data;
using MultiVendorPlatform.Infrastructure.Idempotency;
using MultiVendorPlatform.Infrastructure.Kafka;
using MultiVendorPlatform.Infrastructure.Observability;
using MultiVendorPlatform.Infrastructure.Outbox;
using MultiVendorPlatform.Modules.Cart.Services;
using MultiVendorPlatform.Modules.Catalog.Services;
using MultiVendorPlatform.Modules.Fulfillment.Services;
using MultiVendorPlatform.Modules.Identity.Security;
using MultiVendorPlatform.Modules.Identity.Services;
using MultiVendorPlatform.Modules.Identity.Validators;
using MultiVendorPlatform.Modules.Inventory.Services;
using MultiVendorPlatform.Modules.Notifications.Services;
using MultiVendorPlatform.Modules.Orders.Services;
using MultiVendorPlatform.Modules.Payments.Services;
using MultiVendorPlatform.Modules.Vendors.Services;
using Serilog;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// 1. Serilog Structured Logging
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "MultiVendorPlatform")
    .Enrich.WithProperty("Environment", builder.Environment.EnvironmentName)
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{CorrelationId}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

builder.Host.UseSerilog();

// 2. Database (PostgreSQL + EF Core)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Port=5432;Database=multivendor_db;Username=postgres;Password=postgres";

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseNpgsql(connectionString, npgsql =>
    {
        npgsql.MigrationsAssembly("MultiVendorPlatform.Api");
        npgsql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null);
    });
});

// 3. Memory & Redis Cache
builder.Services.AddMemoryCache();
var redisConnectionString = builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379";
try
{
    var redisMultiplexer = ConnectionMultiplexer.Connect(new ConfigurationOptions
    {
        EndPoints = { redisConnectionString },
        AbortOnConnectFail = false,
        ConnectTimeout = 3000
    });
    builder.Services.AddSingleton<IConnectionMultiplexer>(redisMultiplexer);
}
catch (Exception ex)
{
    Log.Warning(ex, "Failed to connect to Redis on startup. Running with in-memory caching fallback.");
}
builder.Services.AddSingleton<ICacheService, RedisCacheService>();

// 4. Kafka & Messaging Infrastructure
builder.Services.AddSingleton<IKafkaProducer, KafkaProducer>();
builder.Services.AddSingleton<IRetryAndDeadLetterHandler, RetryAndDeadLetterHandler>();
builder.Services.AddScoped<IKafkaEventDispatcher, KafkaEventDispatcher>();

// Outbox Pattern
builder.Services.AddScoped<IOutboxService, OutboxService>();
builder.Services.AddHostedService<OutboxProcessorBackgroundService>();
builder.Services.AddHostedService<KafkaConsumerBackgroundService>();

// 5. Domain & Application Services
builder.Services.AddScoped<IPasswordHasher, PasswordHasher>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IIdentityService, IdentityService>();
builder.Services.AddScoped<IVendorService, VendorService>();
builder.Services.AddScoped<ICatalogService, CatalogService>();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<ICartService, CartService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IOrderSagaCoordinator, OrderSagaCoordinator>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IFulfillmentService, FulfillmentService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IIdempotencyService, IdempotencyService>();

// 6. Background Jobs
builder.Services.AddHostedService<ReservationExpiryBackgroundJob>();
builder.Services.AddHostedService<LowStockMonitoringBackgroundJob>();

// 7. Validation
builder.Services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>();

// 8. OpenTelemetry Tracing
builder.Services.AddAppObservability(builder.Configuration);

// 9. JWT Authentication & Authorization
var jwtSecret = builder.Configuration["Jwt:Secret"] ?? "A_Very_Strong_And_Ultra_Secure_Production_Grade_Secret_Key_At_Least_32_Bytes_Long!";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "MultiVendorPlatform";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "MultiVendorPlatform.Client";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        ValidateIssuer = true,
        ValidIssuer = jwtIssuer,
        ValidateAudience = true,
        ValidAudience = jwtAudience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddAuthorization();

// 10. Rate Limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("fixed", limiterOptions =>
    {
        limiterOptions.PermitLimit = 100;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit = 10;
    });
});

// 11. CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:4200", "http://localhost:8080")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// 12. Controllers & Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Multi-Vendor Order & Fulfillment API",
        Version = "v1",
        Description = "Production-style enterprise API supporting multi-vendor orders, distributed sagas, outbox events, and concurrent inventory reservation."
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and your token.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// 13. Health Checks
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "postgresql", failureStatus: HealthStatus.Unhealthy)
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: new[] { "live" });

var app = builder.Build();

// 14. Middlewares Pipeline
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();

// Security Headers
app.Use(async (context, next) =>
{
    context.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
    context.Response.Headers.TryAdd("X-Frame-Options", "DENY");
    context.Response.Headers.TryAdd("X-XSS-Protection", "1; mode=block");
    context.Response.Headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");
    await next();
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Multi-Vendor API v1");
        c.RoutePrefix = string.Empty; // Serve Swagger UI at application root
    });
}

app.UseCors("AllowFrontend");
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers().RequireRateLimiting("fixed");

// Health check endpoints
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => true
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready") || check.Name == "postgresql"
});
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});

// 15. Automatic Database Initialization & Seeding on Startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    try
    {
        await db.Database.EnsureCreatedAsync();
        Log.Information("Database schema ensured successfully.");
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to initialize database on startup.");
    }
}

app.Run();
