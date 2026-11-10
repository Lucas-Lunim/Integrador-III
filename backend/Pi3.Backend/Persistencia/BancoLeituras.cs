using System.Text;
using MySqlConnector;
using Pi3.Backend.Dados;

namespace Pi3.Backend.Persistencia;

/// <summary>Estado de uma máquina gravado na tabela maquina (status atual do DER).</summary>
public readonly record struct EstadoMaquina(int Maquina, int Status, DateTime StatusDesde, DateTime UltimaLeitura);

/// <summary>Gravação e leitura dos fatos brutos (leitura e valor).</summary>
public static class BancoLeituras
{
    /// <summary>Linhas por INSERT: cabe folgado no pacote padrão do MySQL.</summary>
    const int Bloco = 400;

    /// <summary>Grava o lote inteiro numa transação. INSERT IGNORE: regravar o mesmo lote não duplica nada.</summary>
    public static async Task GravarAsync(MySqlConnection c, Lote l, IReadOnlyList<EstadoMaquina> maquinas, CancellationToken ct)
    {
        await using var tx = await c.BeginTransactionAsync(ct);
        var coletadoEm = Periodo.TruncarMs(l.ColetadoEm);

        for (var inicio = 0; inicio < l.Leituras; inicio += Bloco)
        {
            var fim = Math.Min(l.Leituras, inicio + Bloco);
            await using var cmd = new MySqlCommand { Connection = c, Transaction = tx };
            var sql = new StringBuilder("INSERT IGNORE INTO leitura (instante, maquina_id, status_id, id_api, duracao_ms, coletado_em, qualidade) VALUES ");
            for (var i = inicio; i < fim; i++)
            {
                var j = i - inicio;
                sql.Append(j == 0 ? "" : ",").Append($"(@t{j},@m{j},@s{j},@i{j},@d{j},@c{j},@q{j})");
                cmd.Parameters.AddWithValue($"@t{j}", l.Instante[i]);
                cmd.Parameters.AddWithValue($"@m{j}", l.Maquina[i]);
                cmd.Parameters.AddWithValue($"@s{j}", l.Status[i]);
                cmd.Parameters.AddWithValue($"@i{j}", l.IdApi[i] >= 0 ? l.IdApi[i] : DBNull.Value);
                cmd.Parameters.AddWithValue($"@d{j}", l.DuracaoMs[i] >= 0 ? l.DuracaoMs[i] : DBNull.Value);
                cmd.Parameters.AddWithValue($"@c{j}", coletadoEm);
                cmd.Parameters.AddWithValue($"@q{j}", (int)l.Flags[i]);
            }
            cmd.CommandText = sql.ToString();
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // Cada valor precisa do instante e da máquina da sua leitura (chave composta).
        var leituraDoValor = new int[l.Valor.Length];
        for (var i = 0; i < l.Leituras; i++)
            Array.Fill(leituraDoValor, i, l.PrimeiroValor[i], l.QtdValores[i]);

        for (var inicio = 0; inicio < l.Valor.Length; inicio += Bloco)
        {
            var fim = Math.Min(l.Valor.Length, inicio + Bloco);
            await using var cmd = new MySqlCommand { Connection = c, Transaction = tx };
            var sql = new StringBuilder("INSERT IGNORE INTO valor (instante, maquina_id, atributo_id, valor, incremento) VALUES ");
            for (var k = inicio; k < fim; k++)
            {
                var j = k - inicio;
                var i = leituraDoValor[k];
                sql.Append(j == 0 ? "" : ",").Append($"(@t{j},@m{j},@a{j},@v{j},@n{j})");
                cmd.Parameters.AddWithValue($"@t{j}", l.Instante[i]);
                cmd.Parameters.AddWithValue($"@m{j}", l.Maquina[i]);
                cmd.Parameters.AddWithValue($"@a{j}", l.Atributo[k]);
                cmd.Parameters.AddWithValue($"@v{j}", l.Valor[k]);
                cmd.Parameters.AddWithValue($"@n{j}", double.IsNaN(l.Incremento[k]) ? DBNull.Value : l.Incremento[k]);
            }
            cmd.CommandText = sql.ToString();
            await cmd.ExecuteNonQueryAsync(ct);
        }

        foreach (var m in maquinas)
        {
            await using var cmd = new MySqlCommand(
                "UPDATE maquina SET status_atual_id = @s, status_desde = @d, ultima_leitura = @u WHERE id = @m AND (ultima_leitura IS NULL OR ultima_leitura <= @u)",
                c, tx);
            cmd.Parameters.AddWithValue("@s", m.Status);
            cmd.Parameters.AddWithValue("@d", m.StatusDesde);
            cmd.Parameters.AddWithValue("@u", m.UltimaLeitura);
            cmd.Parameters.AddWithValue("@m", m.Maquina);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    /// <summary>Status e última leitura gravados de cada máquina (para retomar o estado ao reiniciar).</summary>
    public static async Task<List<EstadoMaquina>> CarregarEstadoMaquinasAsync(MySqlConnection c, CancellationToken ct)
    {
        await using var cmd = new MySqlCommand(
            "SELECT id, status_atual_id, status_desde, ultima_leitura FROM maquina WHERE ultima_leitura IS NOT NULL AND status_atual_id IS NOT NULL", c);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var lista = new List<EstadoMaquina>();
        while (await r.ReadAsync(ct))
            lista.Add(new EstadoMaquina(r.GetInt32(0), r.GetInt32(1),
                r.IsDBNull(2) ? default : Periodo.Utc(r.GetDateTime(2)), Periodo.Utc(r.GetDateTime(3))));
        return lista;
    }

    /// <summary>Leituras gravadas a partir de <paramref name="desde"/>, no mesmo formato de um lote recém-coletado.</summary>
    public static async Task<Lote> CarregarJanelaAsync(MySqlConnection c, DateTime desde, CancellationToken ct)
    {
        await using var cmd = new MySqlCommand("""
            SELECT instante, maquina_id, status_id, COALESCE(id_api, -1), COALESCE(duracao_ms, -1), qualidade
              FROM leitura WHERE instante >= @d ORDER BY instante, maquina_id;
            SELECT instante, maquina_id, atributo_id, valor, incremento
              FROM valor WHERE instante >= @d ORDER BY instante, maquina_id, atributo_id;
            """, c);
        cmd.Parameters.AddWithValue("@d", desde);
        await using var r = await cmd.ExecuteReaderAsync(ct);

        var instante = new List<DateTime>();
        var maquina = new List<int>();
        var status = new List<int>();
        var idApi = new List<long>();
        var duracao = new List<int>();
        var flags = new List<Qualidade>();
        while (await r.ReadAsync(ct))
        {
            instante.Add(Periodo.Utc(r.GetDateTime(0)));
            maquina.Add(r.GetInt32(1));
            status.Add(r.GetInt32(2));
            idApi.Add(r.GetInt64(3));
            duracao.Add(r.GetInt32(4));
            flags.Add((Qualidade)r.GetInt32(5));
        }

        await r.NextResultAsync(ct);
        var vInstante = new List<DateTime>();
        var vMaquina = new List<int>();
        var atributo = new List<int>();
        var valor = new List<double>();
        var incremento = new List<double>();
        while (await r.ReadAsync(ct))
        {
            vInstante.Add(Periodo.Utc(r.GetDateTime(0)));
            vMaquina.Add(r.GetInt32(1));
            atributo.Add(r.GetInt32(2));
            valor.Add(r.GetDouble(3));
            incremento.Add(r.IsDBNull(4) ? double.NaN : r.GetDouble(4));
        }

        // As duas consultas vêm na mesma ordem (instante, máquina): os valores de
        // cada leitura formam um trecho contínuo, encontrado numa passada só.
        var primeiro = new int[instante.Count];
        var qtd = new int[instante.Count];
        var k = 0;
        for (var i = 0; i < instante.Count; i++)
        {
            while (k < valor.Count && (vInstante[k] < instante[i] || (vInstante[k] == instante[i] && vMaquina[k] < maquina[i]))) k++;
            primeiro[i] = k;
            while (k < valor.Count && vInstante[k] == instante[i] && vMaquina[k] == maquina[i]) k++;
            qtd[i] = k - primeiro[i];
        }

        return new Lote
        {
            ColetadoEm = DateTime.UtcNow,
            Instante = [.. instante],
            Maquina = [.. maquina],
            Status = [.. status],
            IdApi = [.. idApi],
            DuracaoMs = [.. duracao],
            Flags = [.. flags],
            PrimeiroValor = primeiro,
            QtdValores = qtd,
            Atributo = [.. atributo],
            Valor = [.. valor],
            Incremento = [.. incremento],
        };
    }
}
