using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using SaasStarter.Api.Middleware;
using SaasStarter.Infrastructure;
using SaasStarter.Infrastructure.Data;
using SaasStarter.Infrastructure.Identity;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddProblemDetails();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "SaaS Starter API", Version = "v1" });
    var scheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the JWT returned by /api/auth/login",
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
    };
    options.AddSecurityDefinition("Bearer", scheme);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement { [scheme] = Array.Empty<string>() });
});

builder.Services.AddInfrastructure(builder.Configuration);

// ---- Authentication -------------------------------------------------------
// Default: local accounts (email + password) issuing JWTs.
// Flip AzureAd:Enabled=true and fill in the AzureAd section to accept
// Microsoft Entra ID tokens instead — controllers don't change, they only
// ever read claims (sub, tenant_id, role).
if (builder.Configuration.GetValue<bool>("AzureAd:Enabled"))
{
    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));
}
else
{
    var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
              ?? new JwtOptions();
    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            // Keep claim names exactly as issued (sub, tenant_id, role)
            // instead of the legacy SOAP claim URIs.
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwt.Issuer,
                ValidateAudience = true,
                ValidAudience = jwt.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
                ValidateLifetime = true
            };
        });
}

builder.Services.AddAuthorization(options =>
{
    // Tenant admins = the workspace Owner and Admins. Members are read/write
    // on projects but can't manage people or billing.
    options.AddPolicy("TenantAdmin", policy => policy
        .RequireAuthenticatedUser()
        .RequireAssertion(context =>
        {
            var role = context.User.FindFirst("role")?.Value;
            return role is "Owner" or "Admin";
        }));
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy => policy
        .WithOrigins("http://localhost:5173", "http://localhost:4173")
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();

if (app.Configuration.GetValue("Database:SeedDemo", true))
{
    await DbSeeder.SeedAsync(app.Services);
}

app.UseExceptionHandler();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseCors("Frontend");
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthorization();
app.MapControllers();

app.Run();

// Exposes the entry point to WebApplicationFactory for integration tests.
public partial class Program
{
}
