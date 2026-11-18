using Dairy_Management_System.Data;
using Dairy_Management_System.Models.Auth;
using Dairy_Management_System.Repositories;
using Dairy_Management_System.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;

namespace Dairy_Management_System
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddControllersWithViews();

            // Configure Forwarded Headers for reverse-proxy / load-balancer environments (IIS, Nginx, Docker)
            // Ensures client IP and proto (HTTP vs HTTPS) are accurately identified behind TLS-terminating proxies.
            builder.Services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                // Security rule: default loopback proxies (127.0.0.1, ::1) are trusted.
                // In production with an external reverse proxy network, KnownNetworks and KnownProxies
                // should be explicitly bound to prevent untrusted public clients from spoofing forwarded headers.
            });

            // Configure Cookie Authentication with Role-Aware Redirection
            builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(options =>
                {
                    options.Cookie.Name = "DairyManagement.Auth";
                    options.Cookie.HttpOnly = true;
                    options.Cookie.SameSite = SameSiteMode.Lax;
                    // Development supports local HTTP/HTTPS testing; Production strictly enforces Secure cookies
                    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                        ? CookieSecurePolicy.SameAsRequest
                        : CookieSecurePolicy.Always;
                    options.ExpireTimeSpan = TimeSpan.FromHours(10);
                    options.SlidingExpiration = true;
                    options.LoginPath = "/Auth/AdminLogin";
                    options.AccessDeniedPath = "/Auth/AccessDenied";
                    options.LogoutPath = "/Auth/Logout";

                    // Role-aware login and access-denied redirection events
                    options.Events = new CookieAuthenticationEvents
                    {
                        OnRedirectToLogin = context =>
                        {
                            var requestPath = context.Request.Path.Value ?? string.Empty;
                            var returnUrl = context.Request.Path + context.Request.QueryString;

                            // Route unauthenticated Delivery Boy requests to the field partner login
                            if (RouteClassifier.IsDeliveryBoyPath(requestPath))
                            {
                                var redirectUri = "/Auth/DeliveryBoyLogin?ReturnUrl=" + Uri.EscapeDataString(returnUrl);
                                context.Response.Redirect(redirectUri);
                            }
                            else
                            {
                                // Route Admin and default protected requests to the administrator login
                                var redirectUri = "/Auth/AdminLogin?ReturnUrl=" + Uri.EscapeDataString(returnUrl);
                                context.Response.Redirect(redirectUri);
                            }

                            return Task.CompletedTask;
                        },
                        OnRedirectToAccessDenied = context =>
                        {
                            var returnUrl = context.Request.Path + context.Request.QueryString;
                            var redirectUri = "/Auth/AccessDenied?ReturnUrl=" + Uri.EscapeDataString(returnUrl);
                            context.Response.Redirect(redirectUri);
                            return Task.CompletedTask;
                        }
                    };
                });

            // Configure Role-Based Authorization
            builder.Services.AddAuthorization();

            // Register Cryptographic Password Hasher
            builder.Services.AddScoped<IPasswordHasher<UserAuthRecord>, PasswordHasher<UserAuthRecord>>();

            // Register Centralized SQL Connection Factory (Enforcing least-privilege db_dms_app execution boundary)
            builder.Services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();

            // Register SQL Server ADO.NET Authentication Repository
            builder.Services.AddScoped<IUserAuthRepository>(sp => new UserAuthRepository(
                sp.GetRequiredService<ISqlConnectionFactory>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<UserAuthRepository>>()));

            // Register Authentication Service
            builder.Services.AddScoped<IUserAuthService, UserAuthService>();

            // Register Customer Management ADO.NET Repositories
            builder.Services.AddScoped<ICustomerRepository>(sp => new CustomerRepository(
                sp.GetRequiredService<ISqlConnectionFactory>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CustomerRepository>>()));
            builder.Services.AddScoped<ICustomerSubscriptionRepository>(sp => new CustomerSubscriptionRepository(
                sp.GetRequiredService<ISqlConnectionFactory>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CustomerSubscriptionRepository>>()));
            builder.Services.AddScoped<ICustomerVacationRepository>(sp => new CustomerVacationRepository(
                sp.GetRequiredService<ISqlConnectionFactory>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CustomerVacationRepository>>()));

            // Register Customer Management Application Services
            builder.Services.AddScoped<ICustomerService, CustomerService>();
            builder.Services.AddScoped<ICustomerVacationService, CustomerVacationService>();

            // Register Daily Milk Management ADO.NET Repository
            builder.Services.AddScoped<IDailyMilkRepository>(sp => new DailyMilkRepository(
                sp.GetRequiredService<ISqlConnectionFactory>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<DailyMilkRepository>>()));

            // Register Daily Milk Management Application Service
            builder.Services.AddScoped<IDailyMilkService, DailyMilkService>();

            var app = builder.Build();

            // Enable forwarded headers processing before HttpsRedirection and Authentication
            app.UseForwardedHeaders();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.MapStaticAssets();
            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}")
                .WithStaticAssets();

            // Run initial database initialization and Admin provisioning
            await DbInitializer.InitializeAsync(app.Services);

            await app.RunAsync();
        }
    }

    /// <summary>
    /// Utility class providing maintainable and boundary-accurate route classification
    /// for role-aware authentication redirection. Prevents prefix bleeding such as
    /// classifying '/Home/PrivacyTest' as '/Home/Privacy'.
    /// </summary>
    public static class RouteClassifier
    {
        private static readonly string[] DeliveryBoyBasePaths =
        {
            "/Home/Privacy",
            "/DeliveryBoy"
        };

        /// <summary>
        /// Evaluates whether an HTTP request path targets a Delivery Boy portal route,
        /// strictly enforcing segment boundaries ('/').
        /// </summary>
        /// <param name="path">The HTTP request path.</param>
        /// <returns>True if the path exactly matches or is a subsegment of a Delivery Boy route.</returns>
        public static bool IsDeliveryBoyPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            foreach (var basePath in DeliveryBoyBasePaths)
            {
                if (MatchesPathBoundary(path, basePath))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Checks if a path matches a base route exactly or as a delimited child segment.
        /// Example:
        /// '/Home/Privacy' matches '/Home/Privacy', '/Home/Privacy/', '/Home/Privacy/Sub'
        /// '/Home/Privacy' DOES NOT match '/Home/PrivacyTest' or '/Home/Privacy123'
        /// </summary>
        public static bool MatchesPathBoundary(string path, string basePath)
        {
            if (string.Equals(path, basePath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (path.StartsWith(basePath + "/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }
    }
}
