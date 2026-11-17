namespace Pi3.Backend.Sistemas;

public sealed record SaudeColeta(
    DateTime? UltimaTentativa, bool Ok, string? Erro, int Recebidas, int Novas, int Repetidas, int Invalidas, long TotalNovas);

public sealed record SaudeBanco(bool Ok, string? Erro, int LotesPendentes, int LotesDescartados);

public sealed record SaudeSintese(DateTime? UltimoCiclo, bool Ok, string? Erro, string? Resumo);

public sealed record SaudeDto(DateTime Agora, string ApiUrl, bool Pronto, SaudeColeta Coleta, SaudeBanco Banco,
    SaudeSintese Sintese, double ColetaIntervaloSegundos, double RetencaoBrutoHoras);

/// <summary>Últimos resultados de cada sistema, para a tela e para diagnóstico.</summary>
public sealed class Saude
{
    readonly Lock trava = new();
    SaudeColeta coleta = new(null, false, null, 0, 0, 0, 0, 0);
    SaudeBanco banco = new(false, null, 0, 0);
    SaudeSintese sintese = new(null, false, null, null);

    public void Coleta(DateTime quando, int recebidas, int novas, int repetidas, int invalidas)
    {
        lock (trava) coleta = new(quando, true, null, recebidas, novas, repetidas, invalidas, coleta.TotalNovas + novas);
    }

    public void FalhaColeta(DateTime quando, string erro)
    {
        lock (trava) coleta = coleta with { UltimaTentativa = quando, Ok = false, Erro = erro, Recebidas = 0, Novas = 0, Repetidas = 0 };
    }

    public void Banco(bool ok, string? erro)
    {
        lock (trava) banco = banco with { Ok = ok, Erro = erro };
    }

    public void Pendentes(int pendentes, int descartados)
    {
        lock (trava) banco = banco with { LotesPendentes = pendentes, LotesDescartados = descartados };
    }

    public void Sintese(bool ok, string? erro, string? resumo)
    {
        lock (trava) sintese = new(DateTime.UtcNow, ok, erro, resumo ?? sintese.Resumo);
    }

    public SaudeDto Retrato(Configuracao cfg, bool pronto)
    {
        lock (trava) return new(DateTime.UtcNow, cfg.ApiUrl, pronto, coleta, banco, sintese,
            cfg.ColetaIntervaloSegundos, cfg.RetencaoBrutoHoras);
    }
}
