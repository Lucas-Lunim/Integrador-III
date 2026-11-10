using Pi3.Backend.Dados;
using Pi3.Backend.Transformacoes;

namespace Pi3.Backend.Endpoints;

// Formatos enviados às telas. Séries vão em colunas (um array por campo),
// que é o que os gráficos consomem; NaN vira null no JSON.

public sealed record MaquinaAgoraDto(
    string Codigo, string Tipo, string TipoNome, string Setor,
    string? Status, string? StatusNome, string? Categoria, bool Alerta,
    DateTime? StatusDesde, DateTime? UltimaLeitura, Dictionary<string, double?> Valores);

public sealed record AgoraDto(DateTime GeradoEm, MaquinaAgoraDto[] Maquinas);

public sealed record LeituraDto(string Maquina, DateTime Instante, string Status, Dictionary<string, double> Valores);

public sealed record NovasLeiturasDto(LeituraDto[] Leituras, AgoraDto Agora);

public sealed record SerieDto(string Maquina, string[] Atributos, long[] Instantes, string[] Status, Dictionary<string, double?[]> Series);

public sealed record AtributoDto(string Codigo, string Nome, string? Unidade, string Natureza);

public sealed record StatusDto(string Codigo, string Nome, string Categoria, bool Alerta);

public sealed record TipoDto(string Codigo, string Nome, string[] Atributos);

public sealed record MaquinaDto(string Codigo, string Tipo, string Setor);

public sealed record DimensoesDto(MaquinaDto[] Maquinas, TipoDto[] Tipos, AtributoDto[] Atributos, StatusDto[] Status);

public sealed record GrupoDto(string Codigo, string Nome);

public sealed record IndicadoresDto(
    string Granularidade, DateTime[] Periodos, DateTime[] FimPeriodos, GrupoDto[] Grupos,
    int[] Periodo, int[] Grupo, Dictionary<string, double?[]> Colunas);

public static class Mensagens
{
    /// <summary>Última leitura de cada máquina. Chamar com o estado travado.</summary>
    public static AgoraDto Agora(Dimensoes d, EstadoAoVivo e)
    {
        var maquinas = d.Maquinas();
        var lista = new MaquinaAgoraDto[maquinas.Length];
        for (var j = 0; j < maquinas.Length; j++)
        {
            var m = maquinas[j];
            var conhecida = m < e.CapMaquinas;
            var s = conhecida ? e.StatusAtual[m] : 0;
            var valores = new Dictionary<string, double?>();
            foreach (var a in d.AtributosDoTipo[d.MaquinaTipo[m]])
            {
                var v = conhecida && a < e.CapAtributos ? e.UltimoValor[m * e.CapAtributos + a] : double.NaN;
                valores[d.AtributoCodigo[a]] = double.IsNaN(v) ? null : v;
            }
            var tipo = d.MaquinaTipo[m];
            lista[j] = new MaquinaAgoraDto(
                d.MaquinaCodigo[m], d.TipoCodigo[tipo], d.TipoNome[tipo], d.SetorNome[d.MaquinaSetor[m]],
                s > 0 ? d.StatusCodigo[s] : null, s > 0 ? d.StatusNome[s] : null,
                s > 0 ? Texto(d.StatusCategoria[s]) : null, s > 0 && d.StatusAlerta[s],
                conhecida ? Instante(e.StatusDesde[m]) : null, conhecida ? Instante(e.UltimoInstante[m]) : null,
                valores);
        }
        return new AgoraDto(DateTime.UtcNow, lista);
    }

    /// <summary>Leituras recém-coletadas + retrato atual, enviados pelo SignalR. Chamar com o estado travado.</summary>
    public static NovasLeiturasDto NovasLeituras(Lote l, Dimensoes d, EstadoAoVivo e)
    {
        var leituras = new LeituraDto[l.Leituras];
        for (var i = 0; i < l.Leituras; i++)
        {
            var valores = new Dictionary<string, double>();
            for (var k = l.PrimeiroValor[i]; k < l.PrimeiroValor[i] + l.QtdValores[i]; k++)
                valores[d.AtributoCodigo[l.Atributo[k]]] = l.Valor[k];
            leituras[i] = new LeituraDto(d.MaquinaCodigo[l.Maquina[i]], l.Instante[i], d.StatusCodigo[l.Status[i]], valores);
        }
        return new NovasLeiturasDto(leituras, Agora(d, e));
    }

    /// <summary>Histórico curto de uma máquina, do anel em memória, em ordem cronológica. Chamar com o estado travado.</summary>
    public static SerieDto Serie(Dimensoes d, EstadoAoVivo e, int m, int minutos)
    {
        var atributos = d.AtributosDoTipo[d.MaquinaTipo[m]];
        var instantes = new List<long>();
        var status = new List<string>();
        var colunas = atributos.Select(_ => new List<double?>()).ToArray();

        if (m < e.CapMaquinas)
        {
            var limite = DateTime.UtcNow.AddMinutes(-minutos).Ticks;
            var cap = e.CapHistorico;
            var qtd = e.HistQtd[m];
            var primeira = (e.HistProxima[m] - qtd + cap) % cap;
            for (var j = 0; j < qtd; j++)
            {
                var linha = m * cap + (primeira + j) % cap;
                var t = e.HistInstante[linha];
                if (t < limite) continue;
                instantes.Add((t - DateTime.UnixEpoch.Ticks) / TimeSpan.TicksPerMillisecond);
                status.Add(d.StatusCodigo[e.HistStatus[linha]]);
                for (var c = 0; c < atributos.Length; c++)
                {
                    var a = atributos[c];
                    var v = a < e.CapAtributos ? e.HistValor[linha * e.CapAtributos + a] : double.NaN;
                    colunas[c].Add(double.IsNaN(v) ? null : v);
                }
            }
        }

        var series = new Dictionary<string, double?[]>();
        for (var c = 0; c < atributos.Length; c++) series[d.AtributoCodigo[atributos[c]]] = [.. colunas[c]];
        return new SerieDto(d.MaquinaCodigo[m], [.. atributos.Select(a => d.AtributoCodigo[a])], [.. instantes], [.. status], series);
    }

    public static DimensoesDto Dimensoes(Dimensoes d)
    {
        var tipos = new List<TipoDto>();
        for (var t = 1; t < d.TipoCodigo.Length; t++)
            if (d.TipoCodigo[t].Length > 0)
                tipos.Add(new TipoDto(d.TipoCodigo[t], d.TipoNome[t], [.. d.AtributosDoTipo[t].Select(a => d.AtributoCodigo[a])]));

        var atributos = new List<AtributoDto>();
        for (var a = 1; a < d.AtributoCodigo.Length; a++)
            if (d.AtributoCodigo[a].Length > 0)
                atributos.Add(new AtributoDto(d.AtributoCodigo[a], d.AtributoNome[a], d.AtributoUnidade[a],
                    d.AtributoNatureza[a] == Natureza.Contador ? "CONTADOR" : "MEDIDA"));

        var status = new List<StatusDto>();
        for (var s = 1; s < d.StatusCodigo.Length; s++)
            if (d.StatusCodigo[s].Length > 0)
                status.Add(new StatusDto(d.StatusCodigo[s], d.StatusNome[s], Texto(d.StatusCategoria[s]), d.StatusAlerta[s]));

        var maquinas = d.Maquinas()
            .Select(m => new MaquinaDto(d.MaquinaCodigo[m], d.TipoCodigo[d.MaquinaTipo[m]], d.SetorNome[d.MaquinaSetor[m]]))
            .ToArray();
        return new DimensoesDto(maquinas, [.. tipos], [.. atributos], [.. status]);
    }

    public static IndicadoresDto Indicadores(TabelaIndicadores t) => new(
        Periodo.Texto(t.Granularidade), t.Periodos, t.FimPeriodos,
        [.. t.GrupoCodigo.Select((c, i) => new GrupoDto(c, t.GrupoNome[i]))],
        t.Periodo, t.Grupo,
        t.Colunas.ToDictionary(c => c.Key, c => Nulos(c.Value)));

    static double?[] Nulos(double[] a) => [.. a.Select(v => double.IsNaN(v) ? (double?)null : v)];

    static DateTime? Instante(long ticks) => ticks > 0 ? new DateTime(ticks, DateTimeKind.Utc) : null;

    static string Texto(Categoria c) => c switch
    {
        Categoria.Operacao => "OPERACAO",
        Categoria.Manutencao => "MANUTENCAO",
        Categoria.Parada => "PARADA",
        _ => "DESCONHECIDO"
    };
}
