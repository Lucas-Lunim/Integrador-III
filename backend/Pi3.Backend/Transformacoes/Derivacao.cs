using Pi3.Backend.Dados;

namespace Pi3.Backend.Transformacoes;

/// <summary>
/// <see cref="LoteRecebido"/> → <see cref="Lote"/>: troca códigos por ids,
/// descarta o que já foi recebido, ordena por instante e calcula duração e
/// incrementos. É a única etapa que avança o estado de derivação (último
/// instante e último valor de cada máquina): chamar com o estado travado.
/// </summary>
public static class Derivacao
{
    public static Lote Derivar(LoteRecebido r, Dimensoes d, EstadoAoVivo estado, double limiteLacunaSegundos)
    {
        var limite = (long)(limiteLacunaSegundos * TimeSpan.TicksPerSecond);
        // OrderBy é estável: leituras com o mesmo instante mantêm a ordem da API.
        var ordem = Enumerable.Range(0, r.Count).OrderBy(i => r.Instante[i]).ToArray();

        var instante = new List<DateTime>(r.Count);
        var maquina = new List<int>(r.Count);
        var status = new List<int>(r.Count);
        var idApi = new List<long>(r.Count);
        var duracao = new List<int>(r.Count);
        var flags = new List<Qualidade>(r.Count);
        var primeiro = new List<int>(r.Count);
        var qtd = new List<int>(r.Count);
        var atributo = new List<int>(r.Valor.Count);
        var valor = new List<double>(r.Valor.Count);
        var incremento = new List<double>(r.Valor.Count);
        var repetidas = 0;

        foreach (var i in ordem)
        {
            // Sem id (cadastro falhou com o banco fora do ar): não há como guardar.
            if (!d.MaquinaPorCodigo.TryGetValue(r.Maquina[i], out var m)) continue;

            var t = r.Instante[i].Ticks;
            var anterior = estado.UltimoInstante[m];
            // T04 — a API devolve a janela inteira a cada consulta: o que não é
            // mais novo que a última leitura aceita da máquina já foi recebido.
            if (t <= anterior) { repetidas++; continue; }

            var f = r.Flags[i];
            int ms;
            if (anterior == 0) { ms = -1; f |= Qualidade.PrimeiraLeitura; }
            else if (t - anterior > limite) { ms = -1; f |= Qualidade.LacunaAntes; }
            else ms = (int)((t - anterior) / TimeSpan.TicksPerMillisecond);

            var s = d.StatusPorCodigo.GetValueOrDefault(r.Status[i], d.StatusDesconhecido);
            if (d.StatusCategoria[s] == Categoria.Desconhecido) f |= Qualidade.StatusDesconhecido;

            var tipo = d.MaquinaTipo[m];
            if (r.Tipo[i] is { } tipoInformado && tipoInformado != d.TipoCodigo[tipo]) f |= Qualidade.TipoDivergente;

            var inicioValores = atributo.Count;
            for (var k = r.PrimeiroValor[i]; k < r.PrimeiroValor[i] + r.QtdValores[i]; k++)
            {
                var a = d.Atributo(r.Atributo[k]);
                if (a < 0) continue;
                var v = r.Valor[k];
                var slot = m * estado.CapAtributos + a;
                var inc = double.NaN;
                // T06 — contador: vale a variação desde a leitura anterior, se ela
                // for contínua (ms >= 0). Queda = reinício: variação desconhecida.
                if (d.AtributoNatureza[a] == Natureza.Contador && ms >= 0 && !double.IsNaN(estado.UltimoValor[slot]))
                {
                    var delta = v - estado.UltimoValor[slot];
                    if (delta >= 0) inc = delta;
                    else f |= Qualidade.ReinicioContador;
                }
                estado.UltimoValor[slot] = v;
                atributo.Add(a);
                valor.Add(v);
                incremento.Add(inc);
            }

            // T09 — o tipo costuma enviar estes atributos; a falta é marcada, não preenchida.
            foreach (var esperado in d.AtributosDoTipo[tipo])
            {
                if (atributo.IndexOf(esperado, inicioValores) >= 0) continue;
                f |= Qualidade.AtributoAusente;
                break;
            }

            estado.UltimoInstante[m] = t;
            instante.Add(r.Instante[i]);
            maquina.Add(m);
            status.Add(s);
            idApi.Add(r.IdApi[i]);
            duracao.Add(ms);
            flags.Add(f);
            primeiro.Add(inicioValores);
            qtd.Add(atributo.Count - inicioValores);
        }

        return new Lote
        {
            ColetadoEm = r.ColetadoEm,
            Instante = [.. instante],
            Maquina = [.. maquina],
            Status = [.. status],
            IdApi = [.. idApi],
            DuracaoMs = [.. duracao],
            Flags = [.. flags],
            PrimeiroValor = [.. primeiro],
            QtdValores = [.. qtd],
            Atributo = [.. atributo],
            Valor = [.. valor],
            Incremento = [.. incremento],
            Repetidas = repetidas,
        };
    }
}
