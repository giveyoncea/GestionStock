using System.Text;
using FluentValidation;
using GestionStock.API.Services;
using GestionStock.Application.DTOs;
using GestionStock.Application.Interfaces;
using GestionStock.Application.Services;
using GestionStock.Application.Validators;
using GestionStock.Domain.Interfaces;
using GestionStock.Infrastructure;
using GestionStock.Infrastructure.Data;
using GestionStock.Infrastructure.Identity;
using GestionStock.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

namespace GestionStock.API.Extensions;

public static class ServiceExtensions
{
    public static IServiceCollection AddDatabase(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddHttpContextAccessor();

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("DefaultConnection"),
                sql => sql
                    .MigrationsAssembly("GestionStock.Infrastructure")
                    .EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null)));

        return services;
    }

        public static IServiceCollection AddIdentityAndJwt(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
        {
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = true;
            options.Password.RequiredLength = 10;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.User.RequireUniqueEmail = true;

            // Ne pas exiger confirmation email
            options.SignIn.RequireConfirmedAccount = false;
            options.SignIn.RequireConfirmedEmail = false;
        })
        .AddEntityFrameworkStores<AppDbContext>()
        .AddDefaultTokenProviders();

        var jwtSettings = configuration.GetSection("JwtSettings");
        var key = Encoding.UTF8.GetBytes(jwtSettings["SecretKey"]!);

        // MapInboundClaims = false sur JwtBearer (ci-dessous) suffit Ã  conserver les clÃ©s courtes
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.MapInboundClaims = false; // conserver les clÃ©s courtes du JWT
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtSettings["Issuer"],
                ValidAudience = jwtSettings["Audience"],
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ClockSkew = TimeSpan.Zero,
                NameClaimType = "name",
                RoleClaimType = "role"
            };
        });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("AdminOnly", p => p.RequireRole("Admin"));
            options.AddPolicy("Magasinier", p => p.RequireRole("Admin", "Magasinier", "Superviseur"));
            options.AddPolicy("Acheteur", p => p.RequireRole("Admin", "Acheteur", "Superviseur"));
            options.AddPolicy("Lecteur", p => p.RequireRole("Admin", "Magasinier",
                "Acheteur", "Superviseur", "Lecteur"));
        });

        return services;
    }

    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        // IArticleService remplace par ArticlesAdoController (ADO.NET multi-tenant)
        // IStockService remplace par StocksAdoController (ADO.NET multi-tenant)
        // ICommandeAchatService remplace par CommandesAdoController (ADO.NET multi-tenant)
        // IFournisseurService remplace par FournisseursAdoController (ADO.NET multi-tenant)
        // IDashboardService remplace par DashboardAdoController (ADO.NET multi-tenant)
        services.AddScoped<IInventaireService, InventaireService>();
        services.AddScoped<ICommercialConnectionStringProvider, CommercialConnectionStringProvider>();
        services.AddScoped<ICommercialClientService, ClientCommercialService>();
        services.AddScoped<ICommercialVenteQueryService, VenteCommercialQueryService>();
        services.AddScoped<ICommercialVenteCommandService, VenteCommercialCommandService>();
        services.AddScoped<ICommercialAchatQueryService, AchatCommercialQueryService>();
        services.AddScoped<ICommercialAchatCommandService, AchatCommercialCommandService>();
        services.AddScoped<ICommercialOfflineSyncService, CommercialOfflineSyncService>();
        services.AddScoped<IAuthService, AuthService>();
        // Multi-tenant
        services.AddScoped<ITenantService, TenantService>();
        services.AddScoped<IEmailService, EmailService>();
        services.AddScoped<ProvisioningService>();
        services.AddScoped<IValidator<CreerArticleDto>, CreerArticleValidator>();
        services.AddScoped<IValidator<EntreeStockDto>, EntreeStockValidator>();
        services.AddScoped<IValidator<SortieStockDto>, SortieStockValidator>();
        services.AddScoped<IValidator<CreerCommandeAchatDto>, CreerCommandeValidator>();
        services.AddScoped<IValidator<CreerFournisseurDto>, CreerFournisseurValidator>();
        return services;
    }

    public static IServiceCollection AddSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "GestionStock API",
                Version = "v1",
                Description = "API REST â€“ Gestion de Stock et Approvisionnements (WMS/SCM)"
            });
            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.ApiKey,
                Scheme = "Bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Bearer {token}"
            });
            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme, Id = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });
            c.EnableAnnotations();
        });
        return services;
    }
}

