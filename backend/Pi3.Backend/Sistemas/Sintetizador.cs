using MySqlConnector;
using Pi3.Backend.Dados;
using Pi3.Backend.Persistencia;
using Pi3.Backend.Transformacoes;

namespace Pi3.Backend.Sistemas;

/// <summary>
/// Laço de síntese: brutos → hora → dia → semana/mês → ano, e depois a
/// retenção (apaga o detalhe antigo que já virou resumo).
///
/// Períodos fechados são calculados uma vez e marcados em sintese_controle;
/// os abertos (hora, dia, semana… atuais) são recalculados a cada ciclo, para
/// que o tático e o estratégico vejam o período corrente.
/// </summary>
public sealed class Sintetizador(Configuracao cfg, Banco banco, Memoria memoria, Saude saude, ILogger<Sintetizador> log)
    : BackgroundService
{
    const int MaxPeriodosPorCiclo = 2000;
    static readonly DateTime Origem = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await memoria.AguardarAsync(ct);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(cfg.SinteseIntervaloSegundos));
        do
        {
            try
            {
                saude.Sintese(true, null, await CicloAsync(ct));
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                saude.Sintese(false, ex.Message, null);
                log.LogWarning("Síntese falhou: {Erro}", ex.Message);
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }

    async Task<string> CicloAsync(CancellationToken ct)
    {
        var agora = DateTime.UtcNow;
        var d = memoria.Dimensoes;
        await using var c = await banco.AbrirAsync(ct);
        var controle = await BancoResumos.LerControleAsync(c, ct);

        var recalculados = 0;
        foreach (var g in Periodo.Ordem)
            recalculados += await ProcessarAsync(c, g, controle, agora, d, ct);
        var apagadas = await RetencaoAsync(c, controle, agora, ct);
        return $"{recalculados} períodos recalculados; {apagadas} linhas antigas apagadas";
    }

    async Task<int> ProcessarAsync(MySqlConnection c, Granularidade g, Dictionary<Granularidade, DateTime?> controle,
        DateTime agora, Dimensoes d, CancellationToken ct)
    {
        var fuso = cfg.FusoMinutos;
        var fonte = Periodo.Fonte(g);

        // Primeiro período que ainda pode mudar. Hora: até passar a carência.
        // Demais: enquanto a fonte não estiver fechada até o fim do período.
        DateTime primeiroAberto;
        if (fonte is null) primeiroAberto = Periodo.Inicio(g, agora.AddSeconds(-cfg.SinteseCarenciaSegundos), fuso);
        else if (controle[fonte.Value] is { } fonteAte) primeiroAberto = Periodo.Inicio(g, fonteAte, fuso);
        else primeiroAberto = Origem;

        var atual = Periodo.Inicio(g, agora, fuso);
        var inicio = controle[g];
        if (inicio is null)
        {
            if (await ProximoAsync(c, fonte, Origem, ct) is not { } primeiroDado) return 0;
            inicio = Periodo.Inicio(g, primeiroDado, fuso);
        }

        var periodo = inicio.Value;
        var recalculados = 0;
        while (periodo <= atual && recalculados < MaxPeriodosPorCiclo)
        {
            var fim = Periodo.Fim(g, periodo, fuso);
            var (resumo, status) = await CalcularAsync(c, g, fonte, periodo, fim, d, ct);

            if (resumo.Count == 0 && status.Count == 0)
            {
                // Período vazio (coletor desligado): pula direto para o próximo com dados.
                var proximo = await ProximoAsync(c, fonte, fim, ct);
                var alvo = proximo is null ? primeiroAberto : Min(Periodo.Inicio(g, proximo.Value, fuso), primeiroAberto);
                if (periodo < primeiroAberto) await AvancarAsync(c, g, alvo, controle, ct);
                if (proximo is null) break;
                periodo = Periodo.Inicio(g, proximo.Value, fuso);
                continue;
            }

            await BancoResumos.SubstituirAsync(c, g, periodo, resumo, status, ct);
            recalculados++;
            if (fim <= primeiroAberto) await AvancarAsync(c, g, fim, controle, ct);
            periodo = fim;
        }
        return recalculados;
    }

    async Task<(ColunasResumo, ColunasStatus)> CalcularAsync(MySqlConnection c, Granularidade g, Granularidade? fonte,
        DateTime inicio, DateTime fim, Dimensoes d, CancellationToken ct)
    {
        if (fonte is null)
        {
            var (valores, leituras) = await BancoResumos.LerBrutosAsync(c, inicio, fim, ct);
            var (capM, capA, capS) = Capacidades(d, valores.Maquina, valores.Atributo, leituras.Maquina, leituras.Status);
            return Sintese.DeBrutos(inicio, valores, leituras, capM, capA, capS);
        }
        var (resumo, status) = await BancoResumos.LerResumosAsync(c, fonte.Value, inicio, fim, null, ct);
        var (cm, ca, cs) = Capacidades(d, resumo.Maquina, resumo.Atributo, status.Maquina, status.Status);
        return Sintese.DeResumos(inicio, resumo, status, cm, ca, cs);
    }

    /// <summary>Tamanho dos acumuladores: o maior id conhecido (cadastro ou dado) + 1.</summary>
    static (int, int, int) Capacidades(Dimensoes d, List<int> maquinas, List<int> atributos, List<int> maquinas2, List<int> status) => (
        Math.Max(d.CapacidadeMaquinas, Math.Max(Maior(maquinas), Maior(maquinas2)) + 1),
        Math.Max(d.CapacidadeAtributos, Maior(atributos) + 1),
        Math.Max(d.CapacidadeStatus, Maior(status) + 1));

    static int Maior(List<int> ids) => ids.Count == 0 ? 0 : ids.Max();

    static Task<DateTime?> ProximoAsync(MySqlConnection c, Granularidade? fonte, DateTime desde, CancellationToken ct) =>
        fonte is null ? BancoResumos.ProximoBrutoAsync(c, desde, ct) : BancoResumos.ProximoResumoAsync(c, fonte.Value, desde, ct);

    static async Task AvancarAsync(MySqlConnection c, Granularidade g, DateTime ate,
        Dictionary<Granularidade, DateTime?> controle, CancellationToken ct)
    {
        if (controle[g] is { } atual && ate <= atual) return;
        await BancoResumos.GravarControleAsync(c, g, ate, ct);
        controle[g] = ate;
    }

    /// <summary>T12 — retenção: só apaga o que já está em um resumo fechado.</summary>
    async Task<long> RetencaoAsync(MySqlConnection c, Dictionary<Granularidade, DateTime?> controle, DateTime agora, CancellationToken ct)
    {
        long total = 0;
        if (controle[Granularidade.Hora] is { } horasFechadasAte)
        {
            var limite = Min(agora.AddHours(-cfg.RetencaoBrutoHoras), horasFechadasAte);
            total += await BancoResumos.ApagarAntesAsync(c, "valor", "instante", limite, null, ct);
            total += await BancoResumos.ApagarAntesAsync(c, "leitura", "instante", limite, null, ct);
        }
        if (controle[Granularidade.Dia] is { } diasFechadosAte)
        {
            var limite = Min(agora.AddDays(-cfg.RetencaoHoraDias), diasFechadosAte);
            total += await BancoResumos.ApagarAntesAsync(c, "resumo_atributo", "inicio", limite, Granularidade.Hora, ct);
            total += await BancoResumos.ApagarAntesAsync(c, "resumo_status", "inicio", limite, Granularidade.Hora, ct);
        }
        return total;
    }

    static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
}
