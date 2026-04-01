using Azure;
using CarTitleOCR.Components;
using CarTitleOCR.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Register the Azure AI Document Intelligence OCR service (singleton — stateless).
builder.Services.AddSingleton<IOcrService, DocumentIntelligenceOcrService>();

// Register fraud detection as a singleton so duplicate VIN checks persist while the app is running.
builder.Services.AddSingleton<IFraudDetectionService, FraudDetectionService>();

// Register the Foundry agent service (scoped — one per Blazor circuit for per-user sessions).
// The service creates the AIProjectClient internally from AzureAIFoundry:Endpoint config.
builder.Services.AddScoped<IAgentService, FoundryAgentService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
