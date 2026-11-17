using System.Text;
using MySqlConnector;
using Pi3.Backend.Dados;

namespace Pi3.Backend.Persistencia;

/// <summary>Leitura e gravação dos resumos, do controle da síntese e da retenção.</summary>
public static class BancoResumos
{
    const int Bloco = 400;
    const int BlocoApagar = 20000;

    public static async Task<Dictionary<Granularidade, DateTime?>> LerControleAsync(MySqlConnection c, CancellationToken ct)
    {
        await using var cmd = new MySqlCommand("SELECT granularidade, processado_ate FROM sintese_controle", c);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var controle = Periodo.Ordem.ToDictionary(g => g, _ => (DateTime?)null);
        while (await r.ReadAsync(ct))
            controle[(Granularidade)r.GetByte(0)] = r.IsDBNull(1) ? null : Periodo.Utc(r.GetDateTime(1));
        return controle;
    }

    public static async Task GravarControleAsync(MySqlConnection c, Granularidade g, DateTime processadoAte, CancellationToken ct)
    {
        await using var cmd = new MySqlCommand("UPDATE sintese_controle SET processado_ate = @p WHERE granularidade = @g", c);
        cmd.Parameters.AddWithValue("@p", processadoAte);
        cmd.Parameters.AddWithValue("@g", (byte)g);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public static async Task<(ColunasValores, ColunasLeituras)> LerBrutosAsync(MySqlConnection c, DateTime de, DateTime ate, CancellationToken ct)
    {
        await using var cmd = new MySqlCommand("""
            SELECT instante, maquina_id, atributo_id, valor, incremento
              FROM valor WHERE instante >= @de AND instante < @ate ORDER BY instante, maquina_id, atributo_id;
            SELECT instante, maquina_id, status_id, COALESCE(duracao_ms, -1)
              FROM leitura WHERE instante >= @de AND instante < @ate ORDER BY instante, maquina_id;
            """, c);
        cmd.Parameters.AddWithValue("@de", de);
        cmd.Parameters.AddWithValue("@ate", ate);
        await using var r = await cmd.ExecuteReaderAsync(ct);

        var valores = new ColunasValores();
        while (await r.ReadAsync(ct))
        {
            valores.Instante.Add(Periodo.Utc(r.GetDateTime(0)));
            valores.Maquina.Add(r.GetInt32(1));
            valores.Atributo.Add(r.GetInt32(2));
            valores.Valor.Add(r.GetDouble(3));
            valores.Incremento.Add(r.IsDBNull(4) ? double.NaN : r.GetDouble(4));
        }
        await r.NextResultAsync(ct);
        var leituras = new ColunasLeituras();
        while (await r.ReadAsync(ct))
        {
            leituras.Instante.Add(Periodo.Utc(r.GetDateTime(0)));
            leituras.Maquina.Add(r.GetInt32(1));
            leituras.Status.Add(r.GetInt32(2));
            leituras.DuracaoMs.Add(r.GetInt32(3));
        }
        return (valores, leituras);
    }

    /// <summary>Resumos de uma granularidade com início em [de, ate), em ordem de início (opcionalmente de uma máquina).</summary>
    public static async Task<(ColunasResumo, ColunasStatus)> LerResumosAsync(
        MySqlConnection c, Granularidade g, DateTime de, DateTime ate, int? maquina, CancellationToken ct)
    {
        var filtro = maquina is null ? "" : " AND maquina_id = @m";
        await using var cmd = new MySqlCommand($"""
            SELECT inicio, maquina_id, atributo_id, n, soma, soma_quadrados, minimo, maximo, primeiro, ultimo, incremento
              FROM resumo_atributo WHERE granularidade = @g AND inicio >= @de AND inicio < @ate{filtro}
             ORDER BY inicio, maquina_id, atributo_id;
            SELECT inicio, maquina_id, status_id, segundos, leituras
              FROM resumo_status WHERE granularidade = @g AND inicio >= @de AND inicio < @ate{filtro}
             ORDER BY inicio, maquina_id, status_id;
            """, c);
        cmd.Parameters.AddWithValue("@g", (byte)g);
        cmd.Parameters.AddWithValue("@de", de);
        cmd.Parameters.AddWithValue("@ate", ate);
        if (maquina is not null) cmd.Parameters.AddWithValue("@m", maquina.Value);
        await using var r = await cmd.ExecuteReaderAsync(ct);

        var resumo = new ColunasResumo();
        while (await r.ReadAsync(ct))
        {
            resumo.Inicio.Add(Periodo.Utc(r.GetDateTime(0)));
            resumo.Maquina.Add(r.GetInt32(1));
            resumo.Atributo.Add(r.GetInt32(2));
            resumo.N.Add(r.GetInt64(3));
            resumo.Soma.Add(r.GetDouble(4));
            resumo.SomaQuadrados.Add(r.GetDouble(5));
            resumo.Minimo.Add(r.GetDouble(6));
            resumo.Maximo.Add(r.GetDouble(7));
            resumo.Primeiro.Add(r.GetDouble(8));
            resumo.Ultimo.Add(r.GetDouble(9));
            resumo.Incremento.Add(r.IsDBNull(10) ? double.NaN : r.GetDouble(10));
        }
        await r.NextResultAsync(ct);
        var status = new ColunasStatus();
        while (await r.ReadAsync(ct))
        {
            status.Inicio.Add(Periodo.Utc(r.GetDateTime(0)));
            status.Maquina.Add(r.GetInt32(1));
            status.Status.Add(r.GetInt32(2));
            status.Segundos.Add(r.GetDouble(3));
            status.Leituras.Add(r.GetInt64(4));
        }
        return (resumo, status);
    }

    /// <summary>Primeiro instante com dados brutos a partir de <paramref name="desde"/> (para pular períodos vazios).</summary>
    public static Task<DateTime?> ProximoBrutoAsync(MySqlConnection c, DateTime desde, CancellationToken ct) =>
        Escalar(c, "SELECT MIN(instante) FROM leitura WHERE instante >= @d", desde, null, ct);

    public static Task<DateTime?> ProximoResumoAsync(MySqlConnection c, Granularidade g, DateTime desde, CancellationToken ct) =>
        Escalar(c, "SELECT MIN(inicio) FROM resumo_status WHERE granularidade = @g AND inicio >= @d", desde, g, ct);

    static async Task<DateTime?> Escalar(MySqlConnection c, string sql, DateTime desde, Granularidade? g, CancellationToken ct)
    {
        await using var cmd = new MySqlCommand(sql, c);
        cmd.Parameters.AddWithValue("@d", desde);
        if (g is not null) cmd.Parameters.AddWithValue("@g", (byte)g.Value);
        var resultado = await cmd.ExecuteScalarAsync(ct);
        return resultado is DateTime d ? Periodo.Utc(d) : null;
    }

    /// <summary>Troca os resumos de um período pelos recalculados (períodos abertos são recalculados a cada ciclo).</summary>
    public static async Task SubstituirAsync(MySqlConnection c, Granularidade g, DateTime inicio,
        ColunasResumo resumo, ColunasStatus status, CancellationToken ct)
    {
        await using var tx = await c.BeginTransactionAsync(ct);
        foreach (var tabela in new[] { "resumo_atributo", "resumo_status" })
        {
            await using var apagar = new MySqlCommand($"DELETE FROM {tabela} WHERE granularidade = @g AND inicio = @i", c, tx);
            apagar.Parameters.AddWithValue("@g", (byte)g);
            apagar.Parameters.AddWithValue("@i", inicio);
            await apagar.ExecuteNonQueryAsync(ct);
        }

        for (var ini = 0; ini < resumo.Count; ini += Bloco)
        {
            var fim = Math.Min(resumo.Count, ini + Bloco);
            await using var cmd = new MySqlCommand { Connection = c, Transaction = tx };
            cmd.Parameters.AddWithValue("@g", (byte)g);
            cmd.Parameters.AddWithValue("@i", inicio);
            var sql = new StringBuilder("INSERT INTO resumo_atributo (granularidade, inicio, maquina_id, atributo_id, n, soma, soma_quadrados, minimo, maximo, primeiro, ultimo, incremento) VALUES ");
            for (var k = ini; k < fim; k++)
            {
                var j = k - ini;
                sql.Append(j == 0 ? "" : ",").Append($"(@g,@i,@m{j},@a{j},@n{j},@s{j},@q{j},@mi{j},@ma{j},@p{j},@u{j},@in{j})");
                cmd.Parameters.AddWithValue($"@m{j}", resumo.Maquina[k]);
                cmd.Parameters.AddWithValue($"@a{j}", resumo.Atributo[k]);
                cmd.Parameters.AddWithValue($"@n{j}", resumo.N[k]);
                cmd.Parameters.AddWithValue($"@s{j}", resumo.Soma[k]);
                cmd.Parameters.AddWithValue($"@q{j}", resumo.SomaQuadrados[k]);
                cmd.Parameters.AddWithValue($"@mi{j}", resumo.Minimo[k]);
                cmd.Parameters.AddWithValue($"@ma{j}", resumo.Maximo[k]);
                cmd.Parameters.AddWithValue($"@p{j}", resumo.Primeiro[k]);
                cmd.Parameters.AddWithValue($"@u{j}", resumo.Ultimo[k]);
                cmd.Parameters.AddWithValue($"@in{j}", double.IsNaN(resumo.Incremento[k]) ? DBNull.Value : resumo.Incremento[k]);
            }
            cmd.CommandText = sql.ToString();
            await cmd.ExecuteNonQueryAsync(ct);
        }

        for (var ini = 0; ini < status.Count; ini += Bloco)
        {
            var fim = Math.Min(status.Count, ini + Bloco);
            await using var cmd = new MySqlCommand { Connection = c, Transaction = tx };
            cmd.Parameters.AddWithValue("@g", (byte)g);
            cmd.Parameters.AddWithValue("@i", inicio);
            var sql = new StringBuilder("INSERT INTO resumo_status (granularidade, inicio, maquina_id, status_id, segundos, leituras) VALUES ");
            for (var k = ini; k < fim; k++)
            {
                var j = k - ini;
                sql.Append(j == 0 ? "" : ",").Append($"(@g,@i,@m{j},@s{j},@seg{j},@l{j})");
                cmd.Parameters.AddWithValue($"@m{j}", status.Maquina[k]);
                cmd.Parameters.AddWithValue($"@s{j}", status.Status[k]);
                cmd.Parameters.AddWithValue($"@seg{j}", status.Segundos[k]);
                cmd.Parameters.AddWithValue($"@l{j}", status.Leituras[k]);
            }
            cmd.CommandText = sql.ToString();
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    /// <summary>Apaga em blocos pequenos (não trava a coleta) tudo de <paramref name="tabela"/> anterior a <paramref name="limite"/>.</summary>
    public static async Task<long> ApagarAntesAsync(MySqlConnection c, string tabela, string coluna, DateTime limite,
        Granularidade? g, CancellationToken ct)
    {
        var filtro = g is null ? "" : " AND granularidade = @g";
        long total = 0;
        while (true)
        {
            await using var cmd = new MySqlCommand($"DELETE FROM {tabela} WHERE {coluna} < @l{filtro} LIMIT {BlocoApagar}", c);
            cmd.Parameters.AddWithValue("@l", limite);
            if (g is not null) cmd.Parameters.AddWithValue("@g", (byte)g.Value);
            var apagadas = await cmd.ExecuteNonQueryAsync(ct);
            total += apagadas;
            if (apagadas < BlocoApagar) return total;
        }
    }
}
