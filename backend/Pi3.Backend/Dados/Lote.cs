namespace Pi3.Backend.Dados;

/// <summary>Bits gravados em leitura.qualidade. Cada um corresponde a uma tratativa em docs/tratativas-de-dados.md.</summary>
[Flags]
public enum Qualidade : ushort
{
    Nenhuma = 0,
    /// <summary>T05 — sem leitura anterior conhecida: duração e incrementos desconhecidos.</summary>
    PrimeiraLeitura = 1,
    /// <summary>T05 — intervalo acima do limite: duração e incrementos não são atribuídos.</summary>
    LacunaAntes = 2,
    /// <summary>T06 — algum contador diminuiu: o incremento daquele contador é descartado.</summary>
    ReinicioContador = 4,
    /// <summary>T07 — status ausente ou nunca visto (cadastrado como DESCONHECIDO).</summary>
    StatusDesconhecido = 8,
    /// <summary>T09 — faltou um atributo que o tipo da máquina costuma enviar.</summary>
    AtributoAusente = 16,
    /// <summary>T08 — campo nulo ou não numérico ignorado.</summary>
    ValorInvalido = 32,
    /// <summary>T03 — timestamp ausente, inválido ou absurdo: usado o horário da coleta.</summary>
    InstanteDaColeta = 64,
    /// <summary>T10 — o tipo informado difere do cadastrado para a máquina.</summary>
    TipoDivergente = 128,
}

/// <summary>
/// Uma resposta da API decodificada, ainda com os códigos em texto.
/// Colunas: uma posição por leitura nas listas de leitura; os valores
/// numéricos de cada leitura ocupam um trecho contínuo das listas de valor.
/// </summary>
public sealed class LoteRecebido(DateTime coletadoEm)
{
    public DateTime ColetadoEm { get; } = coletadoEm;

    public readonly List<DateTime> Instante = [];
    public readonly List<string> Maquina = [];
    public readonly List<string?> Tipo = [];
    public readonly List<string> Status = [];
    public readonly List<long> IdApi = [];        // -1 = a leitura não trouxe id
    public readonly List<Qualidade> Flags = [];
    public readonly List<int> PrimeiroValor = [];
    public readonly List<int> QtdValores = [];

    public readonly List<string> Atributo = [];
    public readonly List<double> Valor = [];

    /// <summary>Itens descartados por não terem o campo "maquina".</summary>
    public int LeiturasInvalidas;

    public int Count => Instante.Count;
}

/// <summary>
/// Leituras novas de uma coleta, já com ids, em ordem de instante e com os
/// campos derivados (duração e incremento). É o formato gravado no banco e
/// aplicado ao estado ao vivo.
/// </summary>
public sealed class Lote
{
    public required DateTime ColetadoEm { get; init; }

    public required DateTime[] Instante { get; init; }
    public required int[] Maquina { get; init; }
    public required int[] Status { get; init; }
    public required long[] IdApi { get; init; }
    /// <summary>Milissegundos desde a leitura anterior da mesma máquina; -1 = desconhecida.</summary>
    public required int[] DuracaoMs { get; init; }
    public required Qualidade[] Flags { get; init; }
    public required int[] PrimeiroValor { get; init; }
    public required int[] QtdValores { get; init; }

    public required int[] Atributo { get; init; }
    public required double[] Valor { get; init; }
    /// <summary>Variação desde a leitura anterior (só contadores); NaN = não se aplica ou desconhecida.</summary>
    public required double[] Incremento { get; init; }

    /// <summary>Leituras que já tinham sido recebidas em coletas anteriores.</summary>
    public int Repetidas { get; init; }

    public int Leituras => Instante.Length;
}
