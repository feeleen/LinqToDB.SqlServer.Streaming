using Microsoft.AspNetCore.Http.Features;
using LinqToDB.SqlServer.Streaming.Demo.Services;
using LinqToDB.SqlServer.Streaming;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddScoped<DbContext>(sp => new DbContext(connectionString));

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddScoped<FileService>();

builder.Services.Configure<FormOptions>(options =>
{
	options.MultipartHeadersLengthLimit = 65536; // 64 KB
	options.MultipartBodyLengthLimit = 4L * 1024 * 1024 * 1024; // 4 GB
});

builder.WebHost.ConfigureKestrel(serverOptions =>
{
	serverOptions.Limits.MaxRequestBodySize = 4L * 1024 * 1024 * 1024;
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
	app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

StreamingSettings.UploadBufferSize = 11_000_000;
StreamingSettings.DownloadBufferSize = 81920;
StreamingSettings.PassThroughOperationCancelledExceptions = false;
StreamingSettings.UseArrayPool = true;

app.Run();
