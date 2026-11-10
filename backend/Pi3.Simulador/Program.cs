// Simulador da API industrial — SOMENTE PARA TESTES E DESENVOLVIMENTO.
// Os dados gerados são fictícios: não aponte o backend do banco real para cá.
//
// Imita as rotas da API real (GET /status, /maquinas, /maquinas/{maquina},
// /maquinas/csv e POST /maquinas), guarda as leituras só em memória e apaga as
// mais antigas que 60 s, como a original. Faixas, contadores e status foram
// calibrados com as leituras reais coletadas em 26/10 e 03/11/2026.
//
// Uso:  dotnet run -- [--porta 8090] [--falhas] [--reinicio-minutos 30] [--semente 42]
//   --falhas            injeta problemas de qualidade (campo faltando, nulo, texto, status com acento)
//   --reinicio-minutos  simula a API reiniciando: ids voltam a 1 e os contadores zeram

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

var porta = Opcao(args, "--porta") is { } p ? int.Parse(p) : 8090;
var injetarFalhas = args.Contains("--falhas");
var reinicioMinutos = Opcao(args, "--reinicio-minutos") is { } r ? double.Parse(r, CultureInfo.InvariantCulture) : 0;
var semente = Opcao(args, "--semente") is { } s ? int.Parse(s) : Environment.TickCount;

var builder = WebApplication.CreateBuilder();
builder.WebHost.UseUrls($"http://localhost:{porta}");
builder.Logging.SetMinimumLevel(LogLevel.Warning);
var app = builder.Build();

var simulacao = new Simulacao(new Random(semente), injetarFalhas);
var trava = new Lock();
var ultimoReinicio = DateTime.UtcNow;

_ = Task.Run(async () =>
{
    while (true)
    {
        var agora = DateTime.UtcNow;
        lock (trava)
        {
            if (reinicioMinutos > 0 && (agora - ultimoReinicio).TotalMinutes >= reinicioMinutos)
            {
                simulacao.Reiniciar();
                ultimoReinicio = agora;
                Console.WriteLine($"[{agora:HH:mm:ss}] API simulada reiniciada (ids e contadores zerados).");
            }
            simulacao.Avancar(agora);
        }
        await Task.Delay(250);
    }
});

app.MapGet("/status", () =>
{
    lock (trava) return Results.Json(new { online = true, leituras = simulacao.Leituras.Count, limpezaEmSegundos = 60 });
});
app.MapGet("/maquinas", () =>
{
    lock (trava) return Resposta(simulacao.Leituras);
});
app.MapGet("/maquinas/csv", () =>
{
    lock (trava) return Results.Text(Csv(simulacao.Leituras), "text/csv; charset=utf-8");
});
app.MapGet("/maquinas/{maquina}", (string maquina) =>
{
    lock (trava) return Resposta([.. simulacao.Leituras.Where(l => l["maquina"]?.GetValue<string>() == maquina)]);
});
app.MapPost("/maquinas", (JsonObject leitura) =>
{
    lock (trava) return Results.Json(simulacao.Receber(leitura, DateTime.UtcNow), statusCode: 201);
});

Console.WriteLine($"Simulador da API industrial em http://localhost:{porta}  (falhas: {(injetarFalhas ? "sim" : "não")}, " +
                  $"reinício: {(reinicioMinutos > 0 ? $"a cada {reinicioMinutos} min" : "não")})");
app.Run();

static IResult Resposta(List<JsonObject> leituras)
{
    var dados = new JsonArray([.. leituras.Select(l => l.DeepClone())]);
    return Results.Text(new JsonObject { ["total"] = leituras.Count, ["dados"] = dados }.ToJsonString(), "application/json");
}

static string Csv(List<JsonObject> leituras)
{
    var colunas = leituras.SelectMany(l => l.Select(c => c.Key)).Distinct().ToList();
    var csv = new StringBuilder(string.Join(';', colunas)).Append('\n');
    foreach (var l in leituras)
        csv.Append(string.Join(';', colunas.Select(c => l[c]?.ToString() ?? ""))).Append('\n');
    return csv.ToString();
}

static string? Opcao(string[] args, string nome)
{
    var i = Array.IndexOf(args, nome);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

/// <summary>Estado das seis máquinas e a janela de leituras em memória.</summary>
sealed class Simulacao(Random rnd, bool injetarFalhas)
{
    const double IntervaloSegundos = 5;
    static readonly TimeSpan Janela = TimeSpan.FromSeconds(60);

    public List<JsonObject> Leituras { get; } = [];
    readonly List<Maquina> maquinas = Maquina.Criar(rnd);
    long proximoId = 1;
    DateTime ultimaLimpeza = DateTime.UtcNow;

    public void Reiniciar()
    {
        proximoId = 1;
        Leituras.Clear();
        foreach (var m in maquinas) m.Reiniciar();
    }

    public void Avancar(DateTime agora)
    {
        foreach (var m in maquinas)
        {
            if (m.Proxima == default) m.Proxima = agora.AddSeconds(rnd.NextDouble() * IntervaloSegundos);
            while (m.Proxima <= agora)
            {
                var leitura = m.Gerar(rnd);
                if (injetarFalhas) Estragar(leitura);
                leitura["id"] = proximoId++;
                leitura["timestamp"] = m.Proxima.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
                Leituras.Add(leitura);
                m.Proxima = m.Proxima.AddSeconds(IntervaloSegundos + (rnd.NextDouble() - 0.5) * 0.1);
            }
        }
        // Como a API original: de tempos em tempos descarta o que passou da janela.
        if (agora - ultimaLimpeza >= Janela)
        {
            Leituras.RemoveAll(l => DateTime.Parse(l["timestamp"]!.GetValue<string>(), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal) < agora - Janela);
            ultimaLimpeza = agora;
        }
    }

    public JsonObject Receber(JsonObject leitura, DateTime agora)
    {
        leitura["id"] = proximoId++;
        leitura["timestamp"] = agora.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        Leituras.Add(leitura);
        return leitura;
    }

    /// <summary>Problemas de qualidade para testar as tratativas do backend.</summary>
    void Estragar(JsonObject leitura)
    {
        var campos = leitura.Select(c => c.Key).Where(c => c is not ("maquina" or "tipo" or "status")).ToList();
        var sorteio = rnd.NextDouble();
        if (sorteio < 0.02 && campos.Count > 0) leitura.Remove(campos[rnd.Next(campos.Count)]);
        else if (sorteio < 0.03 && campos.Count > 0) leitura[campos[rnd.Next(campos.Count)]] = null;
        else if (sorteio < 0.04 && campos.Count > 0)
        {
            var campo = campos[rnd.Next(campos.Count)];
            leitura[campo] = leitura[campo]?.ToString().Replace('.', ',');
        }
        else if (sorteio < 0.05 && leitura["status"]?.GetValue<string>() == "MANUTENCAO") leitura["status"] = "Em manutenção";
    }
}

/// <summary>Uma máquina: contadores, medidas e episódios de manutenção/parada.</summary>
sealed class Maquina
{
    public required string Codigo { get; init; }
    public required string Tipo { get; init; }
    public DateTime Proxima;

    readonly Dictionary<string, double> inicial = [];
    readonly Dictionary<string, double> c = [];   // contadores e estados internos
    string episodio = "OPERANDO";
    int restante;

    public static List<Maquina> Criar(Random rnd)
    {
        var lista = new List<Maquina>
        {
            new() { Codigo = "TORNO_CNC_01", Tipo = "TORNO_CNC" },
            new() { Codigo = "PRENSA_HIDRAULICA_01", Tipo = "PRENSA_HIDRAULICA" },
            new() { Codigo = "COMPRESSOR_01", Tipo = "COMPRESSOR_INDUSTRIAL" },
            new() { Codigo = "ESTEIRA_01", Tipo = "ESTEIRA_TRANSPORTADORA" },
            new() { Codigo = "ROBO_SOLDAGEM_01", Tipo = "ROBO_SOLDAGEM" },
            new() { Codigo = "EMBALADORA_01", Tipo = "EMBALADORA" },
        };
        // Valores iniciais das leituras reais de 03/11/2026.
        lista[0].Iniciar(("producao", 1872), ("defeitos", 2), ("desgaste", 4.73));
        lista[1].Iniciar(("ciclos", 3557), ("defeitos", 0), ("desgaste", 3.21));
        lista[2].Iniciar(("desgaste", 3.95), ("horas_operacao", 1200), ("falhas", 0));
        lista[3].Iniciar(("horas_operacao", 0.000146), ("paradas", 1), ("carga", 78.7));
        lista[4].Iniciar(("ciclos", 11684), ("horas_operacao", 0.000406), ("falhas", 1), ("rejeitados", 32));
        lista[5].Iniciar(("producao", 1233), ("horas_operacao", 0.000146), ("falhas", 1), ("rejeitados", 1), ("velocidade", 22));
        return lista;
    }

    void Iniciar(params (string Nome, double Valor)[] valores)
    {
        foreach (var (nome, valor) in valores) inicial[nome] = c[nome] = valor;
    }

    public void Reiniciar()
    {
        foreach (var (nome, valor) in inicial) c[nome] = valor;
        (episodio, restante) = ("OPERANDO", 0);
    }

    public JsonObject Gerar(Random rnd)
    {
        AtualizarEpisodio(rnd);
        var operando = episodio == "OPERANDO";
        double U(double a, double b) => a + rnd.NextDouble() * (b - a);
        double R(double v, int casas = 4) => Math.Round(v, casas);
        var d = c.GetValueOrDefault("desgaste");
        var leitura = new JsonObject { ["maquina"] = Codigo, ["tipo"] = Tipo };

        switch (Tipo)
        {
            case "TORNO_CNC":
                if (operando) { c["producao"] += 18; if (rnd.NextDouble() < 0.02) c["defeitos"]++; c["desgaste"] += U(0, 0.09); }
                leitura["temperatura"] = R(55 + 0.15 * d + U(-1.5, 1.5) - (operando ? 0 : 8));
                leitura["vibracao"] = R(operando ? 1.55 + 0.025 * d + U(-0.1, 0.1) : U(0, 0.05), 5);
                leitura["rotacao"] = operando ? 3176 - Math.Floor(d * 4.4) : 0;
                leitura["producao"] = c["producao"];
                leitura["defeitos"] = c["defeitos"];
                leitura["desgaste"] = R(c["desgaste"], 6);
                break;
            case "PRENSA_HIDRAULICA":
                if (operando) { c["ciclos"] += rnd.Next(30, 41); if (rnd.NextDouble() < 0.02) c["defeitos"]++; c["desgaste"] += U(0, 0.07); }
                leitura["pressao"] = R(operando ? 144 - 0.2 * d + U(-2, 2) : U(0, 2));
                leitura["temperatura"] = R(55.3 + 0.1 * d + U(-1.9, 1.9) - (operando ? 0 : 8), 3);
                leitura["vibracao"] = R(operando ? 1.58 + 0.018 * d + U(-0.1, 0.1) : U(0, 0.05));
                leitura["ciclos"] = c["ciclos"];
                leitura["producao"] = c["ciclos"] - c["defeitos"];
                leitura["defeitos"] = c["defeitos"];
                leitura["desgaste"] = R(c["desgaste"], 6);
                break;
            case "COMPRESSOR_INDUSTRIAL":
                if (operando) c["desgaste"] += U(0.01, 0.07);
                leitura["pressao"] = R(operando ? 8.38 - 0.022 * d + U(-0.1, 0.1) : U(0, 0.5), 6);
                leitura["temperatura"] = R(65.5 + 0.13 * d + U(-1.8, 1.8) - (operando ? 0 : 10));
                leitura["vibracao"] = R(operando ? 2.1 + 0.037 * d + U(-0.1, 0.1) : U(0, 0.05), 6);
                leitura["consumo_kw"] = R(operando ? 6.12 + 0.026 * d + U(-0.1, 0.1) : U(0, 0.2), 6);
                leitura["horas_operacao"] = c["horas_operacao"];
                leitura["desgaste"] = R(c["desgaste"], 6);
                leitura["falhas"] = c["falhas"];
                break;
            case "ESTEIRA_TRANSPORTADORA":
                if (operando) { c["horas_operacao"] += 0.0000014; c["carga"] = Math.Clamp(c["carga"] + U(-6, 6), 30, 85); }
                var carga = operando ? c["carga"] : 0;
                leitura["temperatura"] = R(36 + 0.12 * carga + U(-1, 1), 3);
                leitura["vibracao"] = R(operando ? 1.4 + 0.022 * carga + U(-0.1, 0.1) : U(0, 0.05));
                leitura["horas_operacao"] = R(c["horas_operacao"], 6);
                leitura["velocidade"] = R(operando ? 2.1 - 0.005 * carga + U(-0.08, 0.08) : 0);
                leitura["carga"] = R(carga, 1);
                leitura["paradas"] = c["paradas"];
                break;
            case "ROBO_SOLDAGEM":
                if (operando) { c["ciclos"] += rnd.Next(35, 46); c["horas_operacao"] += 0.0000014; if (rnd.NextDouble() < 0.2) c["rejeitados"]++; }
                var temperatura = Math.Min(90, 88 + U(-2.5, 2.5) - (operando ? 0 : 15));
                leitura["temperatura"] = R(temperatura, 5);
                leitura["ciclos"] = c["ciclos"];
                leitura["horas_operacao"] = R(c["horas_operacao"], 6);
                leitura["falhas"] = c["falhas"];
                leitura["corrente"] = R(operando ? U(16, 20) : 0, 1);
                leitura["rejeitados"] = c["rejeitados"];
                if (operando && temperatura > 85) episodio = "ATENCAO";
                break;
            case "EMBALADORA":
                if (operando)
                {
                    c["producao"] += rnd.Next(10, 16);
                    c["horas_operacao"] += 0.0000014;
                    if (rnd.NextDouble() < 0.05) c["rejeitados"]++;
                    c["velocidade"] = Math.Clamp(c["velocidade"] + U(-2, 2), 20, 30.3);
                }
                leitura["temperatura"] = R(46 + U(-2.3, 2.5) - (operando ? 0 : 6), 3);
                leitura["producao"] = c["producao"];
                leitura["horas_operacao"] = R(c["horas_operacao"], 6);
                leitura["falhas"] = c["falhas"];
                leitura["velocidade"] = R(operando ? c["velocidade"] : 0, 1);
                leitura["rejeitados"] = c["rejeitados"];
                break;
        }
        leitura["status"] = episodio;
        if (episodio == "ATENCAO") episodio = "OPERANDO";
        return leitura;
    }

    /// <summary>De vez em quando a máquina entra em manutenção (2 a 6 min) ou para (1 a 3 min).</summary>
    void AtualizarEpisodio(Random rnd)
    {
        if (restante > 0 && --restante == 0)
        {
            // Hipótese de teste: a manutenção zera o desgaste acumulado.
            if (episodio == "MANUTENCAO" && c.ContainsKey("desgaste")) c["desgaste"] = 0;
            episodio = "OPERANDO";
        }
        if (restante > 0) return;
        var sorteio = rnd.NextDouble();
        if (sorteio < 1 / 2000.0) (episodio, restante) = ("MANUTENCAO", rnd.Next(24, 73));
        else if (sorteio < 1 / 2000.0 + 1 / 1500.0) (episodio, restante) = ("PARADA", rnd.Next(12, 37));
    }
}
