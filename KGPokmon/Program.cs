using KGPokmon.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpClient();
builder.Services.AddScoped<PokemonService>();
builder.Services.AddScoped<ShaclValidationService>();

var pokemonService = new PokemonService(new HttpClient());
await pokemonService.LoadPokemonTranslationsAsync();
var report = await pokemonService.GenerateAllPokemonTripletsAsync();
Console.WriteLine(report);
await pokemonService.SaveGlobalGraphToFileAndSendAsync("wwwroot/data/pokemon.ttl");


var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();


app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
