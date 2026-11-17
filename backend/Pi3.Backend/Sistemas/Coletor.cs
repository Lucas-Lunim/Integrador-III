using Microsoft.AspNetCore.SignalR;
using Pi3.Backend.Dados;
using Pi3.Backend.Endpoints;
using Pi3.Backend.Persistencia;
using Pi3.Backend.Transformacoes;

namespace Pi3.Backend.Sistemas;

/// <summary>
/// Laço de coleta: API → decodificar → cadastrar códigos novos → derivar →
/// estado ao vivo → banco → telas (SignalR). Se o banco cair, os lotes
/// ficam numa fila em memória e o nível operacional continua funcionando.
/// </summary>
public sealed class Coletor(
    Configuracao cfg, Banco banco, Memoria memoria, Saude saude,
    IHttpClientFactory http, IHubContext<OperacionalHub> hub, ILogger<Coletor> log) : BackgroundService
{
    /// <summary>Cerca de 1 hora de coletas a cada 5 s esperando o banco voltar.</summary>
    const int MaxPendentes = 720;

    readonly Queue<(Lote Lote, List<EstadoMaquina> Maquinas)> pendentes = new();
    int descartados;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await InicializarAsync(ct);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(cfg.ColetaIntervaloSegundos));
        do
        {
            try { await CicloAsync(ct); }
            catch (Exception ex) when (!ct.IsCancellationRequested) { log.LogError(ex, "Falha inesperada no ciclo de coleta"); }
        } while (await timer.WaitForNextTickAsync(ct));
    }

    /// <summary>Cria as tabelas, carrega os cadastros e retoma o estado a partir do que já foi gravado.</summary>
    async Task InicializarAsync(CancellationToken ct)
    {
        while (true)
        {
            try
            {
                await banco.AplicarSchemaAsync(ct);
                var d = await banco.CarregarDimensoesAsync(ct);
                await using var c = await banco.AbrirAsync(ct);
                var maquinas = await BancoLeituras.CarregarEstadoMaquinasAsync(c, ct);
                var janela = await BancoLeituras.CarregarJanelaAsync(c, DateTime.UtcNow.AddMinutes(-cfg.MinutosAoVivo), ct);

                var e = memoria.Estado;
                lock (e.Trava)
                {
                    e.GarantirCapacidade(d.CapacidadeMaquinas, d.CapacidadeAtributos);
                    foreach (var m in maquinas)
                    {
                        e.UltimoInstante[m.Maquina] = m.UltimaLeitura.Ticks;
                        e.StatusAtual[m.Maquina] = m.Status;
                        e.StatusDesde[m.Maquina] = m.StatusDesde.Ticks;
                    }
                    e.Registrar(janela);
                    e.RetomarDerivacao(janela);
                }
                memoria.TrocarDimensoes(d);
                saude.Banco(true, null);
                log.LogInformation("Banco pronto: {Maquinas} máquinas cadastradas, {Leituras} leituras recentes carregadas. Coletando de {Api}",
                    d.Maquinas().Length, janela.Leituras, cfg.ApiUrl);
                return;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                saude.Banco(false, ex.Message);
                log.LogError("Não foi possível preparar o banco ({Erro}). Nova tentativa em 5 s.", ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }

    async Task CicloAsync(CancellationToken ct)
    {
        var coletadoEm = DateTime.UtcNow;
        LoteRecebido? recebido = null;
        try
        {
            var json = await http.CreateClient("api").GetByteArrayAsync(cfg.ApiUrl.TrimEnd('/') + "/maquinas", ct);
            recebido = Decodificacao.Ler(json, coletadoEm);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            saude.FalhaColeta(coletadoEm, Descrever(ex));
        }

        if (recebido is not null) await ProcessarAsync(recebido, ct);
        await GravarPendentesAsync(ct);
    }

    async Task ProcessarAsync(LoteRecebido recebido, CancellationToken ct)
    {
        var d = memoria.Dimensoes;
        try
        {
            if (await banco.GarantirCadastrosAsync(recebido, d, ct))
            {
                d = await banco.CarregarDimensoesAsync(ct);
                memoria.TrocarDimensoes(d);
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Sem banco não há id para códigos novos: essas leituras são perdidas; as conhecidas seguem.
            saude.Banco(false, ex.Message);
        }

        Lote lote;
        List<EstadoMaquina> maquinas;
        NovasLeiturasDto? mensagem = null;
        var e = memoria.Estado;
        lock (e.Trava)
        {
            e.GarantirCapacidade(d.CapacidadeMaquinas, d.CapacidadeAtributos);
            lote = Derivacao.Derivar(recebido, d, e, cfg.LimiteLacunaSegundos);
            e.Registrar(lote);
            maquinas = [.. lote.Maquina.Distinct().Select(m => new EstadoMaquina(m, e.StatusAtual[m],
                new DateTime(e.StatusDesde[m], DateTimeKind.Utc), new DateTime(e.UltimoInstante[m], DateTimeKind.Utc)))];
            if (lote.Leituras > 0) mensagem = Mensagens.NovasLeituras(lote, d, e);
        }
        saude.Coleta(recebido.ColetadoEm, recebido.Count, lote.Leituras, lote.Repetidas, recebido.LeiturasInvalidas);
        if (mensagem is null) return;

        pendentes.Enqueue((lote, maquinas));
        while (pendentes.Count > MaxPendentes)
        {
            pendentes.Dequeue();
            descartados++;
        }
        await hub.Clients.All.SendAsync("leituras", mensagem, ct);
    }

    async Task GravarPendentesAsync(CancellationToken ct)
    {
        if (pendentes.Count > 0)
        {
            try
            {
                await using var c = await banco.AbrirAsync(ct);
                while (pendentes.TryPeek(out var item))
                {
                    await BancoLeituras.GravarAsync(c, item.Lote, item.Maquinas, ct);
                    pendentes.Dequeue();
                }
                saude.Banco(true, null);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                saude.Banco(false, ex.Message);
                log.LogWarning("Banco indisponível, {Pendentes} lotes aguardando: {Erro}", pendentes.Count, ex.Message);
            }
        }
        saude.Pendentes(pendentes.Count, descartados);
    }

    static string Descrever(Exception ex) => ex switch
    {
        TaskCanceledException => "A API não respondeu a tempo.",
        HttpRequestException { StatusCode: { } codigo } => $"A API respondeu HTTP {(int)codigo}.",
        HttpRequestException h => $"API inacessível: {h.InnerException?.Message ?? h.Message}",
        System.Text.Json.JsonException => "A resposta da API não é um JSON válido.",
        _ => ex.Message
    };
}
