namespace Pi3.Backend;

/// <summary>Opções da seção "Pi3" do appsettings.json.</summary>
public sealed record Configuracao
{
    /// <summary>Endereço base da API industrial. O IP muda com frequência: ajuste no appsettings.</summary>
    public string ApiUrl { get; init; } = "http://10.12.22.92:8080";

    /// <summary>Intervalo entre consultas à API. Ela apaga as leituras após ~60 s.</summary>
    public double ColetaIntervaloSegundos { get; init; } = 5;

    /// <summary>Acima deste intervalo, duas leituras da mesma máquina não são consideradas contínuas.</summary>
    public double LimiteLacunaSegundos { get; init; } = 60;

    public double SinteseIntervaloSegundos { get; init; } = 60;

    /// <summary>Espera extra antes de fechar uma hora (diferença de relógio e atrasos).</summary>
    public double SinteseCarenciaSegundos { get; init; } = 120;

    /// <summary>Por quanto tempo as leituras brutas ficam no banco antes de restar só o resumo.</summary>
    public double RetencaoBrutoHoras { get; init; } = 168;

    /// <summary>Por quanto tempo os resumos por hora ficam no banco.</summary>
    public double RetencaoHoraDias { get; init; } = 90;

    /// <summary>Fuso usado nos limites de dia, semana, mês e ano, em minutos (Brasília = -180).</summary>
    public int FusoMinutos { get; init; } = -180;

    /// <summary>Quantos minutos de leituras o nível operacional mantém em memória.</summary>
    public int MinutosAoVivo { get; init; } = 60;

    /// <summary>Pasta do Angular compilado que o backend serve (relativa à pasta do projeto).</summary>
    public string PastaFrontend { get; init; } = "../../frontend/dist/painel/browser";
}
