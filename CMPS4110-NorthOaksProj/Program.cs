using System.Text;
using CMPS4110_NorthOaksProj.Data;
using CMPS4110_NorthOaksProj.Data.Services;
using CMPS4110_NorthOaksProj.Data.Services.Chat.Messages;
using CMPS4110_NorthOaksProj.Data.Services.Contracts;
using CMPS4110_NorthOaksProj.Data.Services.DocumentProcessing;
using CMPS4110_NorthOaksProj.Data.Services.Embeddings;
using CMPS4110_NorthOaksProj.Data.Services.Generation;
using CMPS4110_NorthOaksProj.Data.Services.QDrant;
using CMPS4110_NorthOaksProj.Models.Users;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web.UI;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models; // Added for Swagger configuration
using Microsoft.AspNetCore.ResponseCompression;
using CMPS4110_NorthOaksProj.Hubs;



var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Add services to the container.
//builder.Services.AddRazorPages()
//   .AddMicrosoftIdentityUI();

// Add Swagger services with JWT authentication
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "CMPS4110-NorthOaksProj", Version = "v1" });

    // Add JWT Bearer authentication to Swagger
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token in the text input below.",
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
                },
                Scheme = "oauth2",
                Name = "Bearer",
                In = ParameterLocation.Header
            },
            new List<string>()
        }
    });
});

// Database connection
builder.Services.AddDbContextFactory<DataContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Add Identity
builder.Services.AddIdentity<User, IdentityRole<int>>(options =>
{
    // Password requirements configuration
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = true;
    options.Password.RequiredLength = 6;
})
.AddEntityFrameworkStores<DataContext>()
.AddDefaultTokenProviders();

// Dependency Injection for services
builder.Services.AddScoped<IContractsService, ContractsService>();
builder.Services.AddScoped<IChatMessagesService, ChatMessagesService>();
builder.Services.AddScoped<IQdrantService, QdrantService>();
builder.Services.AddScoped<IDocumentProcessingService, DocumentProcessingService>();

// Razor Pages
builder.Services.AddRazorPages()
    .AddMicrosoftIdentityUI();

// Controllers for API endpoints
builder.Services.AddControllers();

// JWT Authentication
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = false,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]))
    };

    // 👇 Allow SignalR to send the token via query string for WebSockets
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) &&
                path.StartsWithSegments("/processingHub"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
});


// Token service
builder.Services.AddScoped<TokenService>();
builder.Services.AddAuthorization();
builder.Services.AddConnections();

// === Embeddings (Ollama MiniLM) ===
builder.Services.Configure<OllamaOptions>(builder.Configuration.GetSection("Ollama"));

builder.Services.AddHttpClient<OllamaEmbeddingClient>((sp, http) =>
{
    var opts = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
    http.BaseAddress = new Uri(opts.BaseUrl);
});


// === Response Generation (Ollama Llama3.2) ===
builder.Services.AddHttpClient<OllamaGenerationClient>((sp, http) =>
{
    var opts = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
    http.BaseAddress = new Uri(opts.BaseUrl);
});

builder.Services.AddScoped<IOllamaGenerationClient>(sp => sp.GetRequiredService<OllamaGenerationClient>());

// expose via interface for DI
builder.Services.AddScoped<IEmbeddingClient>(sp => sp.GetRequiredService<OllamaEmbeddingClient>());
builder.Services.AddScoped<MessageEmbeddingService>();

// SignalR
builder.Services.AddSignalR();
builder.Services.AddResponseCompression(opts =>
{
    opts.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
        ["application/octet-stream"]);
});

// Background task queue services
builder.Services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
builder.Services.AddHostedService<TaskRunner>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseResponseCompression();
app.UseHttpsRedirection();
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapStaticAssets();
app.MapControllers();
app.MapFallbackToFile("index.html");
app.MapHub<ProcessingHub>("/processingHub");

// === Debug endpoints ===
app.MapPost("/debug/embed", async (
    [FromServices] IEmbeddingClient emb,
    [FromBody] EmbedBody body) =>
{
    var v = await emb.EmbedAsync(body.text);
    return Results.Json(new { length = v.Length, first3 = v.Take(3) });
});

app.MapGet("/debug/embed", async (
    [FromServices] IEmbeddingClient emb,
    [FromQuery] string text) =>
{
    var v = await emb.EmbedAsync(text);
    return Results.Json(new { length = v.Length, first3 = v.Take(3) });
});
// === End debug endpoints ===

app.Run();

// declare record *after* app.Run, or in separate file
public record EmbedBody(string text);