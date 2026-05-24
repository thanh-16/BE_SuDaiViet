# TẦNG 1: Dùng SDK .NET 8.0 để biên dịch và tối ưu hóa ứng dụng
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build-env
WORKDIR /app

# Sao chép tệp dự án .csproj và khôi phục các thư viện NuGet (dùng mảng JSON để hỗ trợ khoảng trắng và tiếng Việt)
COPY ["Sử Đại Việt/*.csproj", "./"]
RUN dotnet restore

# Sao chép toàn bộ mã nguồn từ thư mục dự án và xuất bản ứng dụng
COPY ["Sử Đại Việt/", "./"]
RUN dotnet publish -c Release -o out

# TẦNG 2: Dùng Runtime ASP.NET Core 8.0 siêu nhẹ để chạy ở Production
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build-env /app/out .

# Cấu hình cổng truyền tin 8080 mặc định
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# Điểm kích hoạt chính để chạy ứng dụng
ENTRYPOINT ["dotnet", "Sử Đại Việt.dll"]
