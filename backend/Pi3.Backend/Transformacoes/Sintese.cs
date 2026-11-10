using Pi3.Backend.Dados;

namespace Pi3.Backend.Transformacoes;

/// <summary>
/// Cálculo dos resumos de um período. Brutos → hora e resumo → resumo usam o
/// mesmo acumulador: os campos (n, soma, soma dos quadrados, mínimo, máximo,
/// primeiro, último, incremento) são somáveis, então um nível sai exato do
/// nível abaixo. Os dados chegam em ordem de tempo, o que define "primeiro" e
/// "último" sem ordenar nada.
/// </summary>
public static class Sintese
{
    public static (ColunasResumo, ColunasStatus) DeBrutos(
        DateTime inicio, ColunasValores valores, ColunasLeituras leituras, int capMaquinas, int capAtributos, int capStatus)
    {
        var atributos = new AcumuladorAtributos(capMaquinas, capAtributos);
        for (var i = 0; i < valores.Count; i++)
        {
            var v = valores.Valor[i];
            atributos.Mesclar(valores.Maquina[i], valores.Atributo[i], 1, v, v * v, v, v, v, v, valores.Incremento[i]);
        }

        var status = new AcumuladorStatus(capMaquinas, capStatus);
        for (var i = 0; i < leituras.Count; i++)
        {
            // T11 — o intervalo até a leitura conta para o status informado nela.
            var segundos = leituras.DuracaoMs[i] >= 0 ? leituras.DuracaoMs[i] / 1000.0 : 0;
            status.Somar(leituras.Maquina[i], leituras.Status[i], segundos, 1);
        }
        return (atributos.Escrever(inicio), status.Escrever(inicio));
    }

    public static (ColunasResumo, ColunasStatus) DeResumos(
        DateTime inicio, ColunasResumo fonte, ColunasStatus fonteStatus, int capMaquinas, int capAtributos, int capStatus)
    {
        var atributos = new AcumuladorAtributos(capMaquinas, capAtributos);
        for (var i = 0; i < fonte.Count; i++)
            atributos.Mesclar(fonte.Maquina[i], fonte.Atributo[i], fonte.N[i], fonte.Soma[i], fonte.SomaQuadrados[i],
                fonte.Minimo[i], fonte.Maximo[i], fonte.Primeiro[i], fonte.Ultimo[i], fonte.Incremento[i]);

        var status = new AcumuladorStatus(capMaquinas, capStatus);
        for (var i = 0; i < fonteStatus.Count; i++)
            status.Somar(fonteStatus.Maquina[i], fonteStatus.Status[i], fonteStatus.Segundos[i], fonteStatus.Leituras[i]);

        return (atributos.Escrever(inicio), status.Escrever(inicio));
    }

    /// <summary>Um espaço por par (máquina, atributo), indexado por m * capAtributos + a.</summary>
    sealed class AcumuladorAtributos(int capMaquinas, int capAtributos)
    {
        readonly long[] n = new long[capMaquinas * capAtributos];
        readonly double[] soma = new double[capMaquinas * capAtributos];
        readonly double[] quadrados = new double[capMaquinas * capAtributos];
        readonly double[] minimo = new double[capMaquinas * capAtributos];
        readonly double[] maximo = new double[capMaquinas * capAtributos];
        readonly double[] primeiro = new double[capMaquinas * capAtributos];
        readonly double[] ultimo = new double[capMaquinas * capAtributos];
        readonly double[] incremento = new double[capMaquinas * capAtributos];
        readonly bool[] temIncremento = new bool[capMaquinas * capAtributos];
        readonly List<int> usados = [];

        public void Mesclar(int m, int a, long n2, double soma2, double quadrados2, double min2, double max2,
            double primeiro2, double ultimo2, double incremento2)
        {
            var k = m * capAtributos + a;
            if (n[k] == 0)
            {
                usados.Add(k);
                minimo[k] = min2;
                maximo[k] = max2;
                primeiro[k] = primeiro2;
            }
            n[k] += n2;
            soma[k] += soma2;
            quadrados[k] += quadrados2;
            if (min2 < minimo[k]) minimo[k] = min2;
            if (max2 > maximo[k]) maximo[k] = max2;
            ultimo[k] = ultimo2;
            if (!double.IsNaN(incremento2))
            {
                incremento[k] += incremento2;
                temIncremento[k] = true;
            }
        }

        public ColunasResumo Escrever(DateTime inicio)
        {
            usados.Sort();
            var saida = new ColunasResumo();
            foreach (var k in usados)
            {
                saida.Inicio.Add(inicio);
                saida.Maquina.Add(k / capAtributos);
                saida.Atributo.Add(k % capAtributos);
                saida.N.Add(n[k]);
                saida.Soma.Add(soma[k]);
                saida.SomaQuadrados.Add(quadrados[k]);
                saida.Minimo.Add(minimo[k]);
                saida.Maximo.Add(maximo[k]);
                saida.Primeiro.Add(primeiro[k]);
                saida.Ultimo.Add(ultimo[k]);
                saida.Incremento.Add(temIncremento[k] ? incremento[k] : double.NaN);
            }
            return saida;
        }
    }

    /// <summary>Um espaço por par (máquina, status).</summary>
    sealed class AcumuladorStatus(int capMaquinas, int capStatus)
    {
        readonly double[] segundos = new double[capMaquinas * capStatus];
        readonly long[] leituras = new long[capMaquinas * capStatus];
        readonly List<int> usados = [];

        public void Somar(int m, int s, double seg, long qtd)
        {
            var k = m * capStatus + s;
            if (leituras[k] == 0) usados.Add(k);
            segundos[k] += seg;
            leituras[k] += qtd;
        }

        public ColunasStatus Escrever(DateTime inicio)
        {
            usados.Sort();
            var saida = new ColunasStatus();
            foreach (var k in usados)
            {
                saida.Inicio.Add(inicio);
                saida.Maquina.Add(k / capStatus);
                saida.Status.Add(k % capStatus);
                saida.Segundos.Add(segundos[k]);
                saida.Leituras.Add(leituras[k]);
            }
            return saida;
        }
    }
}
