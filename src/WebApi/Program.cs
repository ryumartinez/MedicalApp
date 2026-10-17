using System.Reflection;
using FastEndpoints.Swagger;
using MedicalApp.Host.Extensions;
using MedicalApp.Host;

var appAssembly = Assembly.GetExecutingAssembly();
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddCustomProblemDetails();

builder.AddWebApi();
builder.AddApplication();
builder.AddInfrastructure();

builder.Services.ConfigureFeatures(builder.Configuration, appAssembly);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseCustomFastEndpoints();
app.UseSwaggerGen();
app.UseEventualConsistencyMiddleware();

app.MapDefaultEndpoints();
app.UseExceptionHandler();

app.Run();

namespace MedicalApp
{
    public partial class Program;
}