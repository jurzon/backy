using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using CommitmentsBlazor.Data;
using CommitmentsBlazor.Services;
using Microsoft.AspNetCore.Components.Authorization;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<DevAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<DevAuthStateProvider>());

builder.Services.AddSingleton<WeatherForecastService>();
builder.Services.AddHttpClient<ApiClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration.GetValue<string>("ApiBase") ?? "https://localhost:5001/");
    // Dev basic credentials (dev:dev) to call API
    var bytes = Encoding.UTF8.GetBytes("dev:dev");
    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(bytes));
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
