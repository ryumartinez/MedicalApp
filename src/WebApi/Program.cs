using System.Reflection;
using MedicalApp.Host.Extensions;
using MedicalApp.Host;
using MedicalApp.Common.Storage;

var appAssembly = Assembly.GetExecutingAssembly();
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddCustomProblemDetails();

builder.AddWebApi();
builder.AddKeycloakAuthentication();
builder.AddKeycloakSwagger();
builder.AddApplication();
builder.AddInfrastructure();
builder.AddObjectStorage();

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

app.UseAuthentication();
app.UseAuthorization();
app.UseCustomFastEndpoints();
app.UseKeycloakSwaggerGen();
app.UseEventualConsistencyMiddleware();

app.MapDefaultEndpoints();
app.UseExceptionHandler();

app.Run();

namespace MedicalApp
{
    public partial class Program;
}
