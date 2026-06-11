using Microsoft.EntityFrameworkCore;
using Sử_Đại_Việt.Data;
using PayOS;
using Sử_Đại_Việt.Middleware;
using Sử_Đại_Việt.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// KIỂM TRA BẢO MẬT KHỞI CHẠY (Bảo vệ thông tin bí mật cấu hình)
var dbConn = builder.Configuration.GetConnectionString("SupabaseConnection");
var admKey = builder.Configuration["AdminSettings:AdminKey"];
var jwtSec = builder.Configuration["Supabase:JwtSecret"];

if (dbConn == null || dbConn.Contains("YOUR_DATABASE_PASSWORD"))
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("⚠️ CẢNH BÁO: Connection String cho SupabaseConnection hiện chưa được cấu hình. Vui lòng thiết lập biến môi trường ConnectionStrings__SupabaseConnection.");
    Console.ResetColor();
}
if (string.IsNullOrEmpty(admKey) || admKey == "YOUR_ADMIN_SECRET_KEY")
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("⚠️ CẢNH BÁO: Khóa AdminKey hiện đang dùng giá trị mặc định. Vui lòng thiết lập biến môi trường AdminSettings__AdminKey để bảo vệ API quản lý.");
    Console.ResetColor();
}
if (string.IsNullOrEmpty(jwtSec) || jwtSec == "YOUR_SUPABASE_JWT_SECRET")
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("⚠️ CẢNH BÁO: Supabase JwtSecret hiện đang dùng giá trị mặc định. Vui lòng thiết lập biến môi trường Supabase__JwtSecret.");
    Console.ResetColor();
}

// 1. CẤU HÌNH KẾT NỐI DATABASE SUPABASE POSTGRESQL (EF Core)
var connectionString = builder.Configuration.GetConnectionString("SupabaseConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

// 2. ĐĂNG KÝ DEPENDENCY INJECTION CHO CÁC DỊCH VỤ LOGIC (DI Services)
builder.Services.AddScoped<ILeaderboardService, LeaderboardService>();
builder.Services.AddScoped<IConfigService, ConfigService>();
builder.Services.AddScoped<IAdminLogService, AdminLogService>();
builder.Services.AddScoped<IShopService, ShopService>();
builder.Services.AddScoped<IHeroService, HeroService>();
builder.Services.AddScoped<IMailService, MailService>();
builder.Services.AddScoped<IMarketplaceService, MarketplaceService>();

// Đăng ký PayOSClient cho dịch vụ thanh toán
builder.Services.AddSingleton(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var clientId = configuration["PayOS:ClientId"] ?? throw new InvalidOperationException("PayOS ClientId is missing in appsettings.json.");
    var apiKey = configuration["PayOS:ApiKey"] ?? throw new InvalidOperationException("PayOS ApiKey is missing in appsettings.json.");
    var checksumKey = configuration["PayOS:ChecksumKey"] ?? throw new InvalidOperationException("PayOS ChecksumKey is missing in appsettings.json.");
    return new PayOSClient(clientId, apiKey, checksumKey);
});

// 3. ĐĂNG KÝ DỊCH VỤ HEALTHCHECKS (Giám sát tình trạng hệ thống và kết nối Database)
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ApplicationDbContext>();

// 4. CẤU HÌNH CORS ĐỘNG (Đọc cấu hình an toàn từ appsettings.json)
var allowedOrigins = builder.Configuration.GetSection("CorsSettings:AllowedOrigins").Get<string[]>() 
                     ?? new[] { "http://localhost:5173", "http://localhost:3000" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// 4.5 CẤU HÌNH XÁC THỰC JWT SUPABASE (Google, Facebook...)
var jwtSecret = builder.Configuration["Supabase:JwtSecret"] ?? "YOUR_SUPABASE_JWT_SECRET";
var jwtIssuer = builder.Configuration["Supabase:JwtIssuer"] ?? "https://qcxfmenzyzbwwxrpsdm.supabase.co/auth/v1";
var keyBytes = Encoding.UTF8.GetBytes(jwtSecret);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
        ValidateIssuer = true,
        ValidIssuer = jwtIssuer,
        ValidateAudience = true,
        ValidAudience = "authenticated", // Supabase phát hành JWT luôn gán audience là "authenticated"
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
});

// 4.6 CẤU HÌNH RATE LIMITING CHUYÊN NGHIỆP CHO 10K USERS
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.ContentType = "application/json; charset=utf-8";
        var messageObj = new { message = "Hành động quá nhanh. Nghĩa sĩ vui lòng đợi trong giây lát trước khi tiếp tục lập chiến công!" };
        await context.HttpContext.Response.WriteAsJsonAsync(messageObj, cancellationToken: token);
    };

    // Chính sách 1: ScoreSubmitPolicy cho nghĩa sĩ gửi điểm
    options.AddPolicy("ScoreSubmitPolicy", context =>
    {
        // Nhận diện theo UserId từ JWT Claim sub, nếu chưa đăng nhập thì fallback theo IP
        var userId = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value 
                     ?? context.User.FindFirst("sub")?.Value 
                     ?? context.Connection.RemoteIpAddress?.ToString() 
                     ?? "anonymous-client";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: userId,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 15,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 2,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            });
    });

    // Chính sách 2: AdminApiPolicy cho tác vụ Admin để tránh brute force hoặc lạm dụng API
    options.AddPolicy("AdminApiPolicy", context =>
    {
        var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "anonymous-admin";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: clientIp,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
    });
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// 5. CẤU HÌNH SWAGGER HOÀN HẢO (Với tiêu đề và hỗ trợ Header bảo mật X-Admin-Key trực quan)
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Sử Đại Việt - Web Admin API",
        Version = "v1",
        Description = "Cổng truyền tin REST API phục vụ việc Vinh Danh Bảng Xếp Hạng của Game và Hỗ trợ Web Admin cân bằng chỉ số 3 anh em Tây Sơn."
    });

    // Cấu hình Sơ đồ Bảo mật Header X-Admin-Key trong Swagger
    c.AddSecurityDefinition("ApiKey", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Description = "Mã xác thực Admin cấp cao phục vụ việc cân bằng chỉ số. Điền mã khóa của bạn vào ô Value bên dưới (ví dụ: TaySonNghiaQuanKey1789).",
        Name = "X-Admin-Key",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "ApiKeyScheme"
    });

    // Cấu hình Sơ đồ Bảo mật JWT Bearer Token phục vụ việc gửi điểm số an toàn
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Description = "Nhập mã Token JWT được cấp từ Supabase của bạn theo định dạng: Bearer {token}",
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "ApiKey"
                },
                In = Microsoft.OpenApi.Models.ParameterLocation.Header
            },
            new List<string>()
        },
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                },
                In = Microsoft.OpenApi.Models.ParameterLocation.Header
            },
            new List<string>()
        }
    });
});

var app = builder.Build();

// 6. CẤU HÌNH PIPELINE PIPELINE XỬ LÝ
// Kích hoạt bộ lọc xử lý lỗi toàn cục đầu tiên trong pipeline để bảo vệ mã nguồn
app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Sử Đại Việt API v1");
    });
}

app.UseHttpsRedirection();

// Kích hoạt phân quyền CORS động
app.UseCors("AllowReactApp");

app.UseAuthentication();
app.UseRateLimiter(); // Chặn rate limit sau khi đã xác thực JWT để lấy UserId
app.UseAuthorization();

// Đăng ký API HealthCheck phục vụ DevOps giám sát tình trạng hệ thống
app.MapHealthChecks("/health");

app.MapControllers();

app.Run();
