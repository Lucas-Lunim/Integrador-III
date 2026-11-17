using Microsoft.Extensions.FileProviders;
using Pi3.Backend;
using Pi3.Backend.Endpoints;
using Pi3.Backend.Persistencia;
using Pi3.Backend.Sistemas;

var builder = WebApplication.CreateBuilder(args);

// Senha do banco e ajustes desta máquina ficam fora do Git (ver Documentação/COMO-INICIAR.md).
// Variáveis de ambiente e linha de comando são lidas de novo para continuarem
// valendo por cima do arquivo local (ex.: apontar para o simulador ou outro banco).
builder.Configuration
    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables()
    .AddCommandLine(args);

var cfg = builder.Configuration.GetSection("Pi3").Get<Configuracao>() ?? new Configuracao();
var conexao = builder.Configuration.GetConnectionString("Banco");
if (string.IsNullOrWhiteSpace(conexao))
    throw new InvalidOperationException(
        "Connection string 'Banco' não configurada. Crie o banco com database/01_criar_banco_e_usuario.sql " +
        "e confira backend/Pi3.Backend/appsettings.Local.json (Documentação/COMO-INICIAR.md).");

builder.Services.AddSingleton(cfg);
builder.Services.AddSingleton(new Banco(conexao));
builder.Services.AddSingleton<Memoria>();
builder.Services.AddSingleton<Saude>();
builder.Services.AddHttpClient("api", c => c.Timeout = TimeSpan.FromSeconds(4));
builder.Services.AddSignalR();
builder.Services.AddHostedService<Coletor>();
builder.Services.AddHostedService<Sintetizador>();

var app = builder.Build();

// O Angular compilado (ng build) é servido pelo próprio backend: um só processo na apresentação.
var pastaFrontend = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, cfg.PastaFrontend));
StaticFileOptions? arquivos = null;
if (Directory.Exists(pastaFrontend))
{
    var provedor = new PhysicalFileProvider(pastaFrontend);
    arquivos = new StaticFileOptions { FileProvider = provedor };
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = provedor });
    app.UseStaticFiles(arquivos);
}

app.MapearApi();
if (arquivos is not null) app.MapFallbackToFile("index.html", arquivos);
else app.MapGet("/", () => Results.Text(
    "Backend no ar. O frontend ainda não foi compilado: rode 'npm run build' em frontend/ ou use 'npm start' (porta 4200).",
    "text/plain; charset=utf-8"));

app.Run();
