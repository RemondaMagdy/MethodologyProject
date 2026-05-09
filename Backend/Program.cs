using System.Text;

using Microsoft.AspNetCore.Authentication.JwtBearer;

using Microsoft.AspNetCore.Identity;

using Microsoft.EntityFrameworkCore;

using Microsoft.IdentityModel.Tokens;

using Microsoft.OpenApi;

using RentVibe.Data;

using RentVibe.Hubs;

using RentVibe.Models;

using RentVibe.Services;



var builder = WebApplication.CreateBuilder(args);



// API controllers

builder.Services.AddControllers();



// Swagger

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(c =>

{

    c.SwaggerDoc("v1", new OpenApiInfo { Title = "RentVibe API", Version = "v1" });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme

    {

        Description = "JWT token — enter: Bearer {token}",

        Name = "Authorization",

        In = ParameterLocation.Header,

        Type = SecuritySchemeType.ApiKey,

        Scheme = "Bearer"

    });

    c.AddSecurityRequirement(doc => new OpenApiSecurityRequirement

    {

        {

            new OpenApiSecuritySchemeReference("Bearer", doc),

            new List<string>()

        }

    });

});



// Entity Framework Core + SQL Server

builder.Services.AddDbContext<AppDbContext>(options =>

    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));



// ASP.NET Core Identity

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>

{

    options.Password.RequireDigit = true;

    options.Password.RequiredLength = 6;

    options.Password.RequireNonAlphanumeric = false;

    options.Password.RequireUppercase = true;

    options.Password.RequireLowercase = true;

})

.AddEntityFrameworkStores<AppDbContext>()

.AddDefaultTokenProviders();



// JWT Authentication

var jwtSecret = builder.Configuration["JwtSettings:Secret"]!;

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

        ValidateAudience = true,

        ValidateLifetime = true,

        ValidateIssuerSigningKey = true,

        ValidIssuer = builder.Configuration["JwtSettings:Issuer"],

        ValidAudience = builder.Configuration["JwtSettings:Audience"],

        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))

    };



    // Allow SignalR to receive the token via query string

    options.Events = new JwtBearerEvents

    {

        OnMessageReceived = context =>

        {

            var accessToken = context.Request.Query["access_token"];

            var path = context.HttpContext.Request.Path;

            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))

            {

                context.Token = accessToken;

            }

            return Task.CompletedTask;

        }

    };

});



// Authorization policies

builder.Services.AddAuthorizationBuilder()

    .AddPolicy("AdminOnly", p => p.RequireRole("Admin"))

    .AddPolicy("LandlordOnly", p => p.RequireRole("Landlord"))

    .AddPolicy("TenantOnly", p => p.RequireRole("Tenant"))

    .AddPolicy("LandlordOrAdmin", p => p.RequireRole("Admin", "Landlord"));



// SignalR

builder.Services.AddSignalR();



// Notification service

builder.Services.AddScoped<NotificationService>();



// CORS — allow frontend dev origin when needed

builder.Services.AddCors(options =>
{
    
    options.AddPolicy("AllowFrontend", policy =>
        policy.WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});



var app = builder.Build();



if (!app.Environment.IsDevelopment())

{

    app.UseExceptionHandler("/Error");

    app.UseHsts();

}



app.UseSwagger();

app.UseSwaggerUI(c =>

{

    c.SwaggerEndpoint("/swagger/v1/swagger.json", "RentVibe API v1");

});



// app.UseHttpsRedirection();

app.UseStaticFiles(new StaticFileOptions

{

    OnPrepareResponse = ctx =>

    {

        // Block direct access to sensitive application documents

        if (ctx.File.PhysicalPath?.Contains(Path.Combine("uploads", "documents")) == true)

        {

            ctx.Context.Response.StatusCode = StatusCodes.Status403Forbidden;

            ctx.Context.Response.ContentLength = 0;

            ctx.Context.Response.Body = Stream.Null;

        }

    }

});



// 1. الغي الـ HTTPS Redirection تماماً عشان الدوكر
// app.UseHttpsRedirection(); 


app.UseRouting();

// 2. تفعيل الـ CORS بالسياسة اللي عرفناها فوق
// تأكدي إنك منادية على نفس الاسم "AllowFrontend"
app.UseCors("AllowFrontend"); 

app.UseAuthentication();
app.UseAuthorization();

// 3. تأكدي إن المابنج للمتحكمات موجود
app.MapControllers();

// 4. السطر ده مهم لو بتستخدمي SignalR
app.MapHub<NotificationHub>("/hubs/notifications");

app.MapFallbackToFile("index.html");



using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<AppDbContext>();
        // السطر ده هيتأكد إن قاعدة البيانات RentVibeDb اتكريتت 
        // وهيطبق أي Migrations ناقصة أوتوماتيكياً أول ما الكونتينر يقوم
        context.Database.Migrate();
        Console.WriteLine("Database check: RentVibeDb is ready and migrated.");
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while migrating or seeding the DB.");
    }
}


app.Run();