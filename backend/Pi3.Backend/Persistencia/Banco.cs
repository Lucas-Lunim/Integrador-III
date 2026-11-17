using System.Collections.Frozen;
using MySqlConnector;
using Pi3.Backend.Dados;

namespace Pi3.Backend.Persistencia;

/// <summary>Conexão, criação das tabelas e cadastros (dimensões).</summary>
public sealed class Banco(string conexaoConfigurada)
{
    /// <summary>Setor em que as máquinas novas entram ("Setor1", combinado para a apresentação).</summary>
    const int SetorPadrao = 1;

    /// <summary>
    /// Com o banco fora do ar, cada tentativa de conexão esperaria 15 s (padrão do
    /// driver) e atrasaria a coleta. 3 s basta para um MySQL local, salvo se a
    /// connection string já definir outro valor.
    /// </summary>
    readonly string conexao = conexaoConfigurada.Contains("Timeout", StringComparison.OrdinalIgnoreCase)
        ? conexaoConfigurada
        : new MySqlConnectionStringBuilder(conexaoConfigurada) { ConnectionTimeout = 3 }.ConnectionString;

    public async Task<MySqlConnection> AbrirAsync(CancellationToken ct)
    {
        var c = new MySqlConnection(conexao);
        await c.OpenAsync(ct);
        return c;
    }

    /// <summary>Executa o database/02_schema.sql (embutido no executável). É idempotente.</summary>
    public async Task AplicarSchemaAsync(CancellationToken ct)
    {
        await using var stream = typeof(Banco).Assembly.GetManifestResourceStream("02_schema.sql")
            ?? throw new InvalidOperationException("02_schema.sql não foi embutido no executável.");
        using var leitor = new StreamReader(stream);
        var sql = await leitor.ReadToEndAsync(ct);

        await using var c = await AbrirAsync(ct);
        await using var cmd = new MySqlCommand(sql, c);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Lê todos os cadastros numa ida ao banco e monta as colunas indexadas por id.</summary>
    public async Task<Dimensoes> CarregarDimensoesAsync(CancellationToken ct)
    {
        await using var c = await AbrirAsync(ct);
        await using var cmd = new MySqlCommand("""
            SELECT id, nome FROM setor;
            SELECT id, codigo, nome FROM tipo_maquina;
            SELECT id, codigo, tipo_id, setor_id FROM maquina;
            SELECT id, codigo, nome, unidade, natureza FROM atributo;
            SELECT id, codigo, nome, categoria, alerta FROM status_maquina;
            SELECT tipo_id, atributo_id FROM atributo_tipo_maquina ORDER BY tipo_id, atributo_id;
            """, c);
        await using var r = await cmd.ExecuteReaderAsync(ct);

        var setores = new List<(int Id, string Nome)>();
        while (await r.ReadAsync(ct)) setores.Add((r.GetInt32(0), r.GetString(1)));

        await r.NextResultAsync(ct);
        var tipos = new List<(int Id, string Codigo, string Nome)>();
        while (await r.ReadAsync(ct)) tipos.Add((r.GetInt32(0), r.GetString(1), r.GetString(2)));

        await r.NextResultAsync(ct);
        var maquinas = new List<(int Id, string Codigo, int Tipo, int Setor)>();
        while (await r.ReadAsync(ct)) maquinas.Add((r.GetInt32(0), r.GetString(1), r.GetInt32(2), r.GetInt32(3)));

        await r.NextResultAsync(ct);
        var atributos = new List<(int Id, string Codigo, string Nome, string? Unidade, Natureza Natureza)>();
        while (await r.ReadAsync(ct))
            atributos.Add((r.GetInt32(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3),
                r.GetString(4) == "CONTADOR" ? Natureza.Contador : Natureza.Medida));

        await r.NextResultAsync(ct);
        var status = new List<(int Id, string Codigo, string Nome, Categoria Categoria, bool Alerta)>();
        while (await r.ReadAsync(ct))
            status.Add((r.GetInt32(0), r.GetString(1), r.GetString(2), Enum.Parse<Categoria>(r.GetString(3), ignoreCase: true), r.GetBoolean(4)));

        await r.NextResultAsync(ct);
        var pares = new List<(int Tipo, int Atributo)>();
        while (await r.ReadAsync(ct)) pares.Add((r.GetInt32(0), r.GetInt32(1)));

        var capSetor = Capacidade(setores.Select(x => x.Id));
        var capTipo = Capacidade(tipos.Select(x => x.Id));
        var capMaquina = Capacidade(maquinas.Select(x => x.Id));
        var capAtributo = Capacidade(atributos.Select(x => x.Id));
        var capStatus = Capacidade(status.Select(x => x.Id));

        var setorNome = Textos(capSetor);
        foreach (var s in setores) setorNome[s.Id] = s.Nome;

        var tipoCodigo = Textos(capTipo);
        var tipoNome = Textos(capTipo);
        foreach (var t in tipos) (tipoCodigo[t.Id], tipoNome[t.Id]) = (t.Codigo, t.Nome);
        var atributosDoTipo = new int[capTipo][];
        for (var t = 0; t < capTipo; t++)
            atributosDoTipo[t] = [.. pares.Where(p => p.Tipo == t).Select(p => p.Atributo)];

        var maquinaCodigo = Textos(capMaquina);
        var maquinaTipo = new int[capMaquina];
        var maquinaSetor = new int[capMaquina];
        foreach (var m in maquinas) (maquinaCodigo[m.Id], maquinaTipo[m.Id], maquinaSetor[m.Id]) = (m.Codigo, m.Tipo, m.Setor);

        var atributoCodigo = Textos(capAtributo);
        var atributoNome = Textos(capAtributo);
        var atributoUnidade = new string?[capAtributo];
        var atributoNatureza = new Natureza[capAtributo];
        foreach (var a in atributos)
            (atributoCodigo[a.Id], atributoNome[a.Id], atributoUnidade[a.Id], atributoNatureza[a.Id]) = (a.Codigo, a.Nome, a.Unidade, a.Natureza);

        var statusCodigo = Textos(capStatus);
        var statusNome = Textos(capStatus);
        var statusCategoria = new Categoria[capStatus];
        Array.Fill(statusCategoria, Categoria.Desconhecido);
        var statusAlerta = new bool[capStatus];
        foreach (var s in status)
            (statusCodigo[s.Id], statusNome[s.Id], statusCategoria[s.Id], statusAlerta[s.Id]) = (s.Codigo, s.Nome, s.Categoria, s.Alerta);

        return new Dimensoes
        {
            SetorNome = setorNome,
            TipoCodigo = tipoCodigo,
            TipoNome = tipoNome,
            AtributosDoTipo = atributosDoTipo,
            MaquinaCodigo = maquinaCodigo,
            MaquinaTipo = maquinaTipo,
            MaquinaSetor = maquinaSetor,
            AtributoCodigo = atributoCodigo,
            AtributoNome = atributoNome,
            AtributoUnidade = atributoUnidade,
            AtributoNatureza = atributoNatureza,
            StatusCodigo = statusCodigo,
            StatusNome = statusNome,
            StatusCategoria = statusCategoria,
            StatusAlerta = statusAlerta,
            TipoPorCodigo = tipos.ToFrozenDictionary(t => t.Codigo, t => t.Id),
            MaquinaPorCodigo = maquinas.ToFrozenDictionary(m => m.Codigo, m => m.Id),
            AtributoPorCodigo = atributos.ToFrozenDictionary(a => a.Codigo, a => a.Id),
            StatusPorCodigo = status.ToFrozenDictionary(s => s.Codigo, s => s.Id),
        };
    }

    /// <summary>
    /// T02 — cadastro automático: máquina, tipo, status, atributo ou par
    /// (tipo, atributo) que a API trouxe e o banco ainda não conhece.
    /// Devolve true se cadastrou algo (as dimensões precisam ser recarregadas).
    /// </summary>
    public async Task<bool> GarantirCadastrosAsync(LoteRecebido r, Dimensoes d, CancellationToken ct)
    {
        var tipos = new HashSet<string>();
        var maquinas = new Dictionary<string, string>();
        var status = new HashSet<string>();
        var atributos = new HashSet<string>();
        var pares = new HashSet<(string Maquina, string Atributo)>();

        for (var i = 0; i < r.Count; i++)
        {
            var maquina = r.Maquina[i];
            d.MaquinaPorCodigo.TryGetValue(maquina, out var m);
            if (m == 0)
            {
                var tipo = r.Tipo[i] ?? "SEM_TIPO";
                maquinas.TryAdd(maquina, tipo);
                if (!d.TipoPorCodigo.ContainsKey(tipo)) tipos.Add(tipo);
            }
            if (!d.StatusPorCodigo.ContainsKey(r.Status[i])) status.Add(r.Status[i]);

            var esperados = m > 0 ? d.AtributosDoTipo[d.MaquinaTipo[m]] : [];
            for (var k = r.PrimeiroValor[i]; k < r.PrimeiroValor[i] + r.QtdValores[i]; k++)
            {
                var a = d.Atributo(r.Atributo[k]);
                if (a < 0) atributos.Add(r.Atributo[k]);
                if (m == 0 || a < 0 || Array.IndexOf(esperados, a) < 0) pares.Add((maquina, r.Atributo[k]));
            }
        }
        if (tipos.Count + maquinas.Count + status.Count + atributos.Count + pares.Count == 0) return false;

        await using var c = await AbrirAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        foreach (var t in tipos)
            await Executar(c, tx, "INSERT IGNORE INTO tipo_maquina (codigo, nome) VALUES (@c, @c)", ct, ("@c", t));
        foreach (var (codigo, tipo) in maquinas)
            await Executar(c, tx, "INSERT IGNORE INTO maquina (codigo, tipo_id, setor_id) SELECT @c, id, @s FROM tipo_maquina WHERE codigo = @t",
                ct, ("@c", codigo), ("@t", tipo), ("@s", SetorPadrao));
        foreach (var s in status)
            await Executar(c, tx, "INSERT IGNORE INTO status_maquina (codigo, nome, categoria) VALUES (@c, @c, 'DESCONHECIDO')", ct, ("@c", s));
        foreach (var a in atributos)
            await Executar(c, tx, "INSERT IGNORE INTO atributo (codigo, nome, natureza) VALUES (@c, @c, 'MEDIDA')", ct, ("@c", a));
        foreach (var (maquina, atributo) in pares)
            await Executar(c, tx, """
                INSERT IGNORE INTO atributo_tipo_maquina (tipo_id, atributo_id)
                SELECT m.tipo_id, a.id FROM maquina m JOIN atributo a ON a.codigo = @a WHERE m.codigo = @m
                """, ct, ("@m", maquina), ("@a", atributo));
        await tx.CommitAsync(ct);
        return true;
    }

    static async Task Executar(MySqlConnection c, MySqlTransaction tx, string sql, CancellationToken ct, params (string Nome, object Valor)[] parametros)
    {
        await using var cmd = new MySqlCommand(sql, c, tx);
        foreach (var (nome, valor) in parametros) cmd.Parameters.AddWithValue(nome, valor);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    static int Capacidade(IEnumerable<int> ids) => ids.DefaultIfEmpty(0).Max() + 1;

    static string[] Textos(int n)
    {
        var a = new string[n];
        Array.Fill(a, "");
        return a;
    }
}
