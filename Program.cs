using GrpcProduct.Data;
using GrpcProduct.Model2s;
using GrpcProduct.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddGrpc();
builder.Services.AddDbContext<AppDbContext>(opt =>
         opt.UseMySql(builder.Configuration.GetConnectionString("DefaultConnection"), new MySqlServerVersion(new Version())));

builder.Services.AddDbContext<ErdatabaseContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("ErDbConnection"));
});
builder.Services.AddGrpcReflection();

var MyAllowSpecificOrigins = "_myAllowSpecificOrigins";

builder.Services.AddCors(options => 
{
    options.AddPolicy(name: MyAllowSpecificOrigins,
        policy =>
        {
            //policy.WithOrigins("http://172.16.200.202:8088")
            policy.AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
        });
});

var app = builder.Build();

// ✅ Must add routing BEFORE endpoints
app.UseRouting();
// Configure the HTTP request pipeline.
app.UseCors(MyAllowSpecificOrigins);
app.UseGrpcWeb(); // สำคัญสำหรับ gRPC-Web


// ✅ 4. Map gRPC services with gRPC-Web and CORS
app.UseEndpoints(endpoints =>
{
    endpoints.MapGrpcService<ErService>()
         .EnableGrpcWeb()
         .RequireCors(MyAllowSpecificOrigins);

    endpoints.MapGrpcReflectionService();
});

app.MapGrpcService<GreeterService>();
app.MapGrpcService<ProductService>();
// app.MapGrpcService<ErService>();

app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

app.Urls.Add("http://*:5157");
app.Run();
