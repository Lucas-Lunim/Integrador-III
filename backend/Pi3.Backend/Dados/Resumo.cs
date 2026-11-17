namespace Pi3.Backend.Dados;

/// <summary>Valores brutos de uma faixa de tempo, na ordem física da tabela (por instante).</summary>
public sealed class ColunasValores
{
    public readonly List<DateTime> Instante = [];
    public readonly List<int> Maquina = [];
    public readonly List<int> Atributo = [];
    public readonly List<double> Valor = [];
    public readonly List<double> Incremento = [];   // NaN = NULL
    public int Count => Valor.Count;
}

/// <summary>Leituras brutas de uma faixa de tempo (só o necessário para o tempo em cada status).</summary>
public sealed class ColunasLeituras
{
    public readonly List<DateTime> Instante = [];
    public readonly List<int> Maquina = [];
    public readonly List<int> Status = [];
    public readonly List<int> DuracaoMs = [];        // -1 = NULL
    public int Count => Maquina.Count;
}

/// <summary>Linhas de resumo_atributo. A mesma forma serve para todas as granularidades.</summary>
public sealed class ColunasResumo
{
    public readonly List<DateTime> Inicio = [];
    public readonly List<int> Maquina = [];
    public readonly List<int> Atributo = [];
    public readonly List<long> N = [];
    public readonly List<double> Soma = [];
    public readonly List<double> SomaQuadrados = [];
    public readonly List<double> Minimo = [];
    public readonly List<double> Maximo = [];
    public readonly List<double> Primeiro = [];
    public readonly List<double> Ultimo = [];
    public readonly List<double> Incremento = [];    // NaN = NULL
    public int Count => N.Count;
}

/// <summary>Linhas de resumo_status.</summary>
public sealed class ColunasStatus
{
    public readonly List<DateTime> Inicio = [];
    public readonly List<int> Maquina = [];
    public readonly List<int> Status = [];
    public readonly List<double> Segundos = [];
    public readonly List<long> Leituras = [];
    public int Count => Maquina.Count;
}
