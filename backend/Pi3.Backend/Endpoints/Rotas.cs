using Microsoft.AspNetCore.SignalR;
using Pi3.Backend.Dados;
using Pi3.Backend.Persistencia;
using Pi3.Backend.Sistemas;
using Pi3.Backend.Transformacoes;

namespace Pi3.Backend.Endpoints;

/// <summary>Canal de tempo real do nível operacional. O coletor envia o evento "leituras".</summary>
public sealed class OperacionalHub : Hub;

/// <summary>
/// Rotas HTTP, uma família por nível da pirâmide. Quanto mais alto o nível,
/// mais agregado o dado: operacional lê a memória; tático e estratégico leem
/// os resumos, cada um nas granularidades que lhe cabem.
/// </summary>
public static class Rotas
{
    static readonly Granularidade[] Tatico = [Granularidade.Semana, Granularidade.Mes];
    static readonly Granularidade[] Estrategico = [Granularidade.Mes, Granularidade.Ano];

    public static void MapearApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/saude", (Saude saude, Memoria memoria, Configuracao cfg) => saude.Retrato(cfg, memoria.Pronta));

        api.MapGet("/dimensoes", (Memoria memoria) =>
            memoria.Pronta ? Results.Ok(Mensagens.Dimensoes(memoria.Dimensoes)) : Inicializando());

        api.MapGet("/operacional/agora", (Memoria memoria) =>
        {
            if (!memoria.Pronta) return Inicializando();
            var (d, e) = (memoria.Dimensoes, memoria.Estado);
            lock (e.Trava) return Results.Ok(Mensagens.Agora(d, e));
        });

        api.MapGet("/operacional/maquinas/{codigo}/serie", (string codigo, int? minutos, Memoria memoria, Configuracao cfg) =>
        {
            if (!memoria.Pronta) return Inicializando();
            var (d, e) = (memoria.Dimensoes, memoria.Estado);
            if (!d.MaquinaPorCodigo.TryGetValue(codigo, out var m)) return Results.NotFound(new { erro = $"Máquina {codigo} não cadastrada." });
            var janela = Math.Clamp(minutos ?? cfg.MinutosAoVivo, 1, cfg.MinutosAoVivo);
            lock (e.Trava) return Results.Ok(Mensagens.Serie(d, e, m, janela));
        });

        api.MapGet("/operacional/maquinas/{codigo}/historico",
            async (string codigo, DateTimeOffset? desde, DateTimeOffset? ate, Memoria memoria, Banco banco, CancellationToken ct) =>
            {
                if (!memoria.Pronta) return Inicializando();
                var d = memoria.Dimensoes;
                if (!d.MaquinaPorCodigo.TryGetValue(codigo, out var m)) return Results.NotFound();
                if (desde > ate) return Results.BadRequest(new { erro = "O início deve anteceder o fim do período." });
                await using var c = await banco.AbrirAsync(ct);
                var dados = await BancoLeituras.LerHistoricoAsync(c, m, desde?.UtcDateTime, ate?.UtcDateTime, ct);
                return Results.Ok(Mensagens.Historico(d, m, dados));
            });

        api.MapGet("/tatico/indicadores",
            (string? granularidade, int? periodos, string? maquina, string? agrupar, Memoria memoria, Banco banco, Configuracao cfg, CancellationToken ct) =>
                IndicadoresAsync(granularidade, periodos ?? 8, maquina,
                    agrupar?.ToLowerInvariant() == "planta" ? Agrupamento.Planta : Agrupamento.Maquina,
                    Tatico, memoria, banco, cfg, ct));

        api.MapGet("/estrategico/indicadores",
            (string? granularidade, int? periodos, string? agrupar, Memoria memoria, Banco banco, Configuracao cfg, CancellationToken ct) =>
                IndicadoresAsync(granularidade, periodos ?? 12, null, agrupar?.ToLowerInvariant() switch
                {
                    "tipo" => Agrupamento.Tipo,
                    "maquina" => Agrupamento.Maquina,
                    _ => Agrupamento.Planta
                }, Estrategico, memoria, banco, cfg, ct));

        app.MapHub<OperacionalHub>("/hubs/operacional");
    }

    static async Task<IResult> IndicadoresAsync(string? granularidade, int periodos, string? maquina, Agrupamento agrupamento,
        Granularidade[] permitidas, Memoria memoria, Banco banco, Configuracao cfg, CancellationToken ct)
    {
        if (!memoria.Pronta) return Inicializando();
        var g = Periodo.DeTexto(granularidade) ?? permitidas[0];
        if (!permitidas.Contains(g))
            return Results.BadRequest(new { erro = $"Granularidade permitida neste nível: {string.Join(", ", permitidas.Select(Periodo.Texto))}." });

        var d = memoria.Dimensoes;
        var maquinas = d.MaquinasMonitoradas();
        int? filtro = null;
        if (!string.IsNullOrWhiteSpace(maquina))
        {
            if (!d.MaquinaPorCodigo.TryGetValue(maquina, out var m) || !maquinas.Contains(m))
                return Results.NotFound(new { erro = $"Máquina {maquina} não está no monitoramento." });
            (maquinas, filtro) = ([m], m);
        }

        var agora = DateTime.UtcNow;
        var inicios = Periodo.Ultimos(g, Math.Clamp(periodos, 1, 60), agora, cfg.FusoMinutos);
        var ate = Periodo.Fim(g, inicios[^1], cfg.FusoMinutos);
        await using var c = await banco.AbrirAsync(ct);
        var (resumo, status) = await BancoResumos.LerResumosAsync(c, g, inicios[0], ate, filtro, ct);
        var tabela = Indicadores.Calcular(g, inicios, agora, cfg.FusoMinutos, resumo, status, d, agrupamento, maquinas);
        return Results.Ok(Mensagens.Indicadores(tabela));
    }

    static IResult Inicializando() =>
        Results.Json(new { erro = "Backend inicializando: aguardando o banco de dados." }, statusCode: StatusCodes.Status503ServiceUnavailable);
}
