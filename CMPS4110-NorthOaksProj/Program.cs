using CMPS4110_NorthOaksProj.Data;
using CMPS4110_NorthOaksProj.Data.Base;
using CMPS4110_NorthOaksProj.Data.Services;
using CMPS4110_NorthOaksProj.Data.Services.Chat.Messages;
using CMPS4110_NorthOaksProj.Data.Services.Contracts;
using CMPS4110_NorthOaksProj.Data.Services.DocumentProcessing;
using CMPS4110_NorthOaksProj.Data.Services.Embeddings;
using CMPS4110_NorthOaksProj.Data.Services.QDrant;
using CMPS4110_NorthOaksProj.Models.Contracts;
using CMPS4110_NorthOaksProj.Models.Users;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Components.WebAssembly.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web.UI;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models; // Added for Swagger configuration
using System.Text;



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
builder.Services.AddSingleton<IQdrantService, QdrantService>();
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
        //   ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]))
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

// expose via interface for DI
builder.Services.AddScoped<IEmbeddingClient>(sp => sp.GetRequiredService<OllamaEmbeddingClient>());



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

app.UseHttpsRedirection();
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapStaticAssets();
app.MapControllers();
app.MapFallbackToFile("index.html");

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