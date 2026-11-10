using Pi3.Backend.Dados;

namespace Pi3.Backend.Transformacoes;

public enum Agrupamento { Maquina, Tipo, Planta }

/// <summary>
/// Indicadores em formato de tabela de colunas: cada linha é um par
/// (período, grupo) e cada indicador é um array alinhado às linhas.
/// NaN = sem dado (a máquina não mede aquilo ou não houve leitura).
/// </summary>
public sealed class TabelaIndicadores
{
    public required Granularidade Granularidade { get; init; }
    public required DateTime[] Periodos { get; init; }
    public required DateTime[] FimPeriodos { get; init; }
    public required string[] GrupoCodigo { get; init; }
    public required string[] GrupoNome { get; init; }
    public required int[] Periodo { get; init; }
    public required int[] Grupo { get; init; }
    public required Dictionary<string, double[]> Colunas { get; init; }
}

/// <summary>
/// Resumos → indicadores de negócio (definições em docs/tratativas-de-dados.md, seção Indicadores).
/// Duas passagens: primeiro por (período, máquina), depois a redução por grupo.
/// </summary>
public static class Indicadores
{
    public static TabelaIndicadores Calcular(
        Granularidade g, DateTime[] periodos, DateTime agoraUtc, int fusoMinutos,
        ColunasResumo resumo, ColunasStatus status, Dimensoes d, Agrupamento agrupamento, int[] maquinas)
    {
        var nP = periodos.Length;
        var capM = d.CapacidadeMaquinas;
        var indicePeriodo = new Dictionary<DateTime, int>(nP);
        for (var p = 0; p < nP; p++) indicePeriodo[periodos[p]] = p;

        // ---- 1ª passagem: colunas por (período, máquina), posição p * capM + m
        var tamanho = nP * capM;
        var producao = Preencher(tamanho, double.NaN);
        var defeitos = Preencher(tamanho, double.NaN);
        var rejeitados = Preencher(tamanho, double.NaN);
        var ciclos = Preencher(tamanho, double.NaN);
        var falhas = Preencher(tamanho, double.NaN);
        var paradas = Preencher(tamanho, double.NaN);
        var desgaste = Preencher(tamanho, double.NaN);
        var tempSoma = new double[tamanho];
        var tempN = new double[tamanho];
        var tempMax = Preencher(tamanho, double.NaN);
        var vibSoma = new double[tamanho];
        var vibN = new double[tamanho];
        var vibMax = Preencher(tamanho, double.NaN);
        var hOperacao = new double[tamanho];
        var hAlerta = new double[tamanho];
        var hManutencao = new double[tamanho];
        var hParada = new double[tamanho];
        var hSemStatus = new double[tamanho];

        var (aProducao, aDefeitos, aRejeitados, aCiclos) = (d.Atributo("producao"), d.Atributo("defeitos"), d.Atributo("rejeitados"), d.Atributo("ciclos"));
        var (aFalhas, aParadas, aDesgaste) = (d.Atributo("falhas"), d.Atributo("paradas"), d.Atributo("desgaste"));
        var (aTemperatura, aVibracao) = (d.Atributo("temperatura"), d.Atributo("vibracao"));

        for (var i = 0; i < resumo.Count; i++)
        {
            if (!indicePeriodo.TryGetValue(resumo.Inicio[i], out var p)) continue;
            var k = p * capM + resumo.Maquina[i];
            var a = resumo.Atributo[i];
            var inc = resumo.Incremento[i];
            if (a == aProducao) Somar(producao, k, inc);
            else if (a == aDefeitos) Somar(defeitos, k, inc);
            else if (a == aRejeitados) Somar(rejeitados, k, inc);
            else if (a == aCiclos) Somar(ciclos, k, inc);
            else if (a == aFalhas) Somar(falhas, k, inc);
            else if (a == aParadas) Somar(paradas, k, inc);
            else if (a == aDesgaste) Somar(desgaste, k, inc);
            else if (a == aTemperatura) { tempSoma[k] += resumo.Soma[i]; tempN[k] += resumo.N[i]; tempMax[k] = Maior(tempMax[k], resumo.Maximo[i]); }
            else if (a == aVibracao) { vibSoma[k] += resumo.Soma[i]; vibN[k] += resumo.N[i]; vibMax[k] = Maior(vibMax[k], resumo.Maximo[i]); }
        }

        for (var i = 0; i < status.Count; i++)
        {
            if (!indicePeriodo.TryGetValue(status.Inicio[i], out var p)) continue;
            var k = p * capM + status.Maquina[i];
            var s = status.Status[i];
            var horas = status.Segundos[i] / 3600.0;
            switch (d.StatusCategoria[s])
            {
                case Categoria.Operacao: hOperacao[k] += horas; if (d.StatusAlerta[s]) hAlerta[k] += horas; break;
                case Categoria.Manutencao: hManutencao[k] += horas; break;
                case Categoria.Parada: hParada[k] += horas; break;
                default: hSemStatus[k] += horas; break;
            }
        }

        // ---- grupos
        var grupoDe = new int[capM];
        string[] grupoCodigo, grupoNome;
        switch (agrupamento)
        {
            case Agrupamento.Maquina:
                grupoCodigo = [.. maquinas.Select(m => d.MaquinaCodigo[m])];
                grupoNome = [.. grupoCodigo];
                for (var j = 0; j < maquinas.Length; j++) grupoDe[maquinas[j]] = j;
                break;
            case Agrupamento.Tipo:
                var tipos = maquinas.Select(m => d.MaquinaTipo[m]).Distinct().Order().ToArray();
                grupoCodigo = [.. tipos.Select(t => d.TipoCodigo[t])];
                grupoNome = [.. tipos.Select(t => d.TipoNome[t])];
                foreach (var m in maquinas) grupoDe[m] = Array.IndexOf(tipos, d.MaquinaTipo[m]);
                break;
            default:
                grupoCodigo = ["PLANTA"];
                grupoNome = ["Planta (todas as máquinas)"];
                break;
        }
        var nG = grupoCodigo.Length;
        var maquinasNoGrupo = new int[nG];
        foreach (var m in maquinas) maquinasNoGrupo[grupoDe[m]]++;

        // ---- 2ª passagem: redução por (período, grupo), linha p * nG + g
        var linhas = nP * nG;
        var col = new Dictionary<string, double[]>();
        double[] Coluna(string nome, double inicial) => col[nome] = Preencher(linhas, inicial);
        var oProducao = Coluna("producao", double.NaN);
        var oPerdas = Coluna("perdas", double.NaN);
        var oCiclos = Coluna("ciclos", double.NaN);
        var oFalhas = Coluna("falhas", double.NaN);
        var oParadas = Coluna("paradas", double.NaN);
        var oDesgaste = Coluna("desgaste", double.NaN);
        var oTempMax = Coluna("temperaturaMax", double.NaN);
        var oVibMax = Coluna("vibracaoMax", double.NaN);
        var oOperacao = Coluna("horasOperacao", 0);
        var oAlerta = Coluna("horasAlerta", 0);
        var oManutencao = Coluna("horasManutencao", 0);
        var oParada = Coluna("horasParada", 0);
        var oSemStatus = Coluna("horasSemStatus", 0);
        var basePerdas = new double[linhas];
        var perdasComBase = new double[linhas];
        var tSoma = new double[linhas];
        var tN = new double[linhas];
        var vSoma = new double[linhas];
        var vN = new double[linhas];

        for (var p = 0; p < nP; p++)
        {
            foreach (var m in maquinas)
            {
                var k = p * capM + m;
                var r = p * nG + grupoDe[m];
                Somar(oProducao, r, producao[k]);
                Somar(oCiclos, r, ciclos[k]);
                Somar(oFalhas, r, falhas[k]);
                Somar(oParadas, r, paradas[k]);
                Somar(oDesgaste, r, desgaste[k]);

                // Perdas = defeitos + rejeitados. Base da taxa: produção + perdas
                // (peças processadas) ou, sem produção, os ciclos da máquina.
                var perdasMaquina = SomaNaN(defeitos[k], rejeitados[k]);
                Somar(oPerdas, r, perdasMaquina);
                var baseMaquina = !double.IsNaN(producao[k])
                    ? producao[k] + (double.IsNaN(perdasMaquina) ? 0 : perdasMaquina)
                    : ciclos[k];
                if (!double.IsNaN(perdasMaquina) && !double.IsNaN(baseMaquina))
                {
                    perdasComBase[r] += perdasMaquina;
                    basePerdas[r] += baseMaquina;
                }

                tSoma[r] += tempSoma[k]; tN[r] += tempN[k]; oTempMax[r] = Maior(oTempMax[r], tempMax[k]);
                vSoma[r] += vibSoma[k]; vN[r] += vibN[k]; oVibMax[r] = Maior(oVibMax[r], vibMax[k]);
                oOperacao[r] += hOperacao[k];
                oAlerta[r] += hAlerta[k];
                oManutencao[r] += hManutencao[k];
                oParada[r] += hParada[k];
                oSemStatus[r] += hSemStatus[k];
            }
        }

        var oTaxa = Coluna("taxaPerdas", double.NaN);
        var oTempMedia = Coluna("temperaturaMedia", double.NaN);
        var oVibMedia = Coluna("vibracaoMedia", double.NaN);
        var oObservadas = Coluna("horasObservadas", 0);
        var oPeriodo = Coluna("horasPeriodo", 0);
        var oDisponibilidade = Coluna("disponibilidade", double.NaN);
        var oCobertura = Coluna("cobertura", double.NaN);
        var fims = new DateTime[nP];
        var periodoDaLinha = new int[linhas];
        var grupoDaLinha = new int[linhas];
        for (var p = 0; p < nP; p++)
        {
            fims[p] = Periodo.Fim(g, periodos[p], fusoMinutos);
            var horasDecorridas = Math.Max(0, ((fims[p] < agoraUtc ? fims[p] : agoraUtc) - periodos[p]).TotalHours);
            for (var gi = 0; gi < nG; gi++)
            {
                var r = p * nG + gi;
                periodoDaLinha[r] = p;
                grupoDaLinha[r] = gi;
                if (basePerdas[r] > 0) oTaxa[r] = perdasComBase[r] / basePerdas[r];
                if (tN[r] > 0) oTempMedia[r] = tSoma[r] / tN[r];
                if (vN[r] > 0) oVibMedia[r] = vSoma[r] / vN[r];
                var conhecido = oOperacao[r] + oManutencao[r] + oParada[r];
                oObservadas[r] = conhecido + oSemStatus[r];
                oPeriodo[r] = horasDecorridas * maquinasNoGrupo[gi];
                if (conhecido > 0) oDisponibilidade[r] = oOperacao[r] / conhecido;
                if (oPeriodo[r] > 0) oCobertura[r] = Math.Min(1, oObservadas[r] / oPeriodo[r]);
            }
        }

        return new TabelaIndicadores
        {
            Granularidade = g,
            Periodos = periodos,
            FimPeriodos = fims,
            GrupoCodigo = grupoCodigo,
            GrupoNome = grupoNome,
            Periodo = periodoDaLinha,
            Grupo = grupoDaLinha,
            Colunas = col,
        };
    }

    static double[] Preencher(int n, double valor)
    {
        var a = new double[n];
        Array.Fill(a, valor);
        return a;
    }

    /// <summary>Soma ignorando NaN; o resultado só é NaN se nenhuma parcela existir.</summary>
    static void Somar(double[] destino, int i, double valor)
    {
        if (double.IsNaN(valor)) return;
        destino[i] = double.IsNaN(destino[i]) ? valor : destino[i] + valor;
    }

    static double SomaNaN(double a, double b) =>
        double.IsNaN(a) ? b : double.IsNaN(b) ? a : a + b;

    static double Maior(double atual, double valor) =>
        double.IsNaN(valor) ? atual : double.IsNaN(atual) || valor > atual ? valor : atual;
}
