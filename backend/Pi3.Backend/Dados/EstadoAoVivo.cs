namespace Pi3.Backend.Dados;

/// <summary>
/// O que o nível operacional precisa, em memória: última leitura de cada
/// máquina e um histórico curto em anel. Também guarda o estado da derivação
/// (último instante e último valor), que o coletor usa para calcular
/// duração e incrementos.
///
/// Layout em colunas planas: índice da máquina = id; matrizes
/// [máquina, atributo] viram um array só, na posição m * CapAtributos + a.
/// Um único escritor (o coletor); leitores usam <see cref="Trava"/>.
/// </summary>
public sealed class EstadoAoVivo(int capHistorico)
{
    public readonly object Trava = new();
    public readonly int CapHistorico = capHistorico;
    public int CapMaquinas { get; private set; }
    public int CapAtributos { get; private set; }

    // por máquina
    public long[] UltimoInstante = [];   // ticks UTC; 0 = nunca
    public int[] StatusAtual = [];       // id; 0 = sem status
    public long[] StatusDesde = [];      // ticks UTC

    // [m * CapAtributos + a]; NaN = sem valor
    public double[] UltimoValor = [];

    // anel: [m * CapHistorico + posição]; valores em [(m * CapHistorico + posição) * CapAtributos + a]
    public long[] HistInstante = [];
    public int[] HistStatus = [];
    public double[] HistValor = [];
    public int[] HistProxima = [];       // próxima posição de escrita por máquina
    public int[] HistQtd = [];

    public void GarantirCapacidade(int maquinas, int atributos)
    {
        if (maquinas <= CapMaquinas && atributos <= CapAtributos) return;
        Redimensionar(Math.Max(CapMaquinas, ProximaPotencia(maquinas)), Math.Max(CapAtributos, ProximaPotencia(atributos)));
    }

    /// <summary>Aplica as leituras novas (já em ordem de instante): status e histórico.</summary>
    public void Registrar(Lote lote)
    {
        for (var i = 0; i < lote.Leituras; i++)
        {
            var m = lote.Maquina[i];
            var t = lote.Instante[i].Ticks;
            if (StatusAtual[m] != lote.Status[i] || StatusDesde[m] == 0)
            {
                StatusAtual[m] = lote.Status[i];
                StatusDesde[m] = t;
            }

            var posicao = HistProxima[m];
            var linha = m * CapHistorico + posicao;
            HistInstante[linha] = t;
            HistStatus[linha] = lote.Status[i];
            Array.Fill(HistValor, double.NaN, linha * CapAtributos, CapAtributos);
            for (var k = lote.PrimeiroValor[i]; k < lote.PrimeiroValor[i] + lote.QtdValores[i]; k++)
                HistValor[linha * CapAtributos + lote.Atributo[k]] = lote.Valor[k];
            HistProxima[m] = (posicao + 1) % CapHistorico;
            if (HistQtd[m] < CapHistorico) HistQtd[m]++;
        }
    }

    /// <summary>Usado na inicialização: retoma o estado de derivação a partir das leituras já gravadas.</summary>
    public void RetomarDerivacao(Lote lote)
    {
        for (var i = 0; i < lote.Leituras; i++)
        {
            var m = lote.Maquina[i];
            UltimoInstante[m] = lote.Instante[i].Ticks;
            for (var k = lote.PrimeiroValor[i]; k < lote.PrimeiroValor[i] + lote.QtdValores[i]; k++)
                UltimoValor[m * CapAtributos + lote.Atributo[k]] = lote.Valor[k];
        }
    }

    void Redimensionar(int nm, int na)
    {
        var (om, oa) = (CapMaquinas, CapAtributos);

        UltimoInstante = Copiar(UltimoInstante, nm);
        StatusAtual = Copiar(StatusAtual, nm);
        StatusDesde = Copiar(StatusDesde, nm);
        HistProxima = Copiar(HistProxima, nm);
        HistQtd = Copiar(HistQtd, nm);
        HistInstante = Copiar(HistInstante, nm * CapHistorico);
        HistStatus = Copiar(HistStatus, nm * CapHistorico);

        var ultimo = new double[nm * na];
        Array.Fill(ultimo, double.NaN);
        for (var m = 0; m < om; m++)
            Array.Copy(UltimoValor, m * oa, ultimo, m * na, oa);
        UltimoValor = ultimo;

        var hist = new double[nm * CapHistorico * na];
        Array.Fill(hist, double.NaN);
        for (var linha = 0; linha < om * CapHistorico; linha++)
            Array.Copy(HistValor, linha * oa, hist, linha * na, oa);
        HistValor = hist;

        (CapMaquinas, CapAtributos) = (nm, na);
    }

    static T[] Copiar<T>(T[] origem, int tamanho)
    {
        var destino = new T[tamanho];
        Array.Copy(origem, destino, origem.Length);
        return destino;
    }

    static int ProximaPotencia(int n)
    {
        var p = 8;
        while (p < n) p *= 2;
        return p;
    }
}
