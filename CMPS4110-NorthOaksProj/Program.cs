using CMPS4110_NorthOaksProj.Data;
using CMPS4110_NorthOaksProj.Data.Base;
using CMPS4110_NorthOaksProj.Data.Services;
using CMPS4110_NorthOaksProj.Models.Users;
using CMPS4110_NorthOaksProj.Models.Contracts;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web.UI;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models; // Added for Swagger configuration
using System.Text;
using CMPS4110_NorthOaksProj.Data.Services.Contracts;
using CMPS4110_NorthOaksProj.Data.Services.Chat.Messages;
using Microsoft.AspNetCore.Components.WebAssembly.Server;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddDbContext<DataContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"))
);

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

app.Run();