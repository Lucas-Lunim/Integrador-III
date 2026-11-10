namespace Pi3.Backend.Dados;

/// <summary>Níveis de síntese. O valor numérico é o id da tabela granularidade.</summary>
public enum Granularidade : byte { Hora = 1, Dia = 2, Semana = 3, Mes = 4, Ano = 5 }

/// <summary>
/// Limites de período. Todo instante é UTC; dia, semana (segunda a domingo),
/// mês e ano começam à meia-noite do fuso configurado.
/// </summary>
public static class Periodo
{
    public static readonly Granularidade[] Ordem =
        [Granularidade.Hora, Granularidade.Dia, Granularidade.Semana, Granularidade.Mes, Granularidade.Ano];

    public static DateTime Inicio(Granularidade g, DateTime utc, int fusoMinutos)
    {
        var local = utc.AddMinutes(fusoMinutos);
        var inicioLocal = g switch
        {
            Granularidade.Hora => new DateTime(local.Year, local.Month, local.Day, local.Hour, 0, 0),
            Granularidade.Dia => local.Date,
            Granularidade.Semana => local.Date.AddDays(-(((int)local.DayOfWeek + 6) % 7)),
            Granularidade.Mes => new DateTime(local.Year, local.Month, 1),
            Granularidade.Ano => new DateTime(local.Year, 1, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(g))
        };
        return Utc(inicioLocal.AddMinutes(-fusoMinutos));
    }

    public static DateTime Fim(Granularidade g, DateTime inicioUtc, int fusoMinutos)
    {
        var local = inicioUtc.AddMinutes(fusoMinutos);
        var fimLocal = g switch
        {
            Granularidade.Hora => local.AddHours(1),
            Granularidade.Dia => local.AddDays(1),
            Granularidade.Semana => local.AddDays(7),
            Granularidade.Mes => local.AddMonths(1),
            Granularidade.Ano => local.AddYears(1),
            _ => throw new ArgumentOutOfRangeException(nameof(g))
        };
        return Utc(fimLocal.AddMinutes(-fusoMinutos));
    }

    /// <summary>Os <paramref name="quantidade"/> períodos mais recentes, terminando no atual (aberto).</summary>
    public static DateTime[] Ultimos(Granularidade g, int quantidade, DateTime agoraUtc, int fusoMinutos)
    {
        var inicios = new DateTime[quantidade];
        var atual = Inicio(g, agoraUtc, fusoMinutos);
        for (var i = quantidade - 1; i >= 0; i--)
        {
            inicios[i] = atual;
            atual = Inicio(g, atual.AddTicks(-1), fusoMinutos);
        }
        return inicios;
    }

    /// <summary>De qual nível cada granularidade é calculada (null = dos dados brutos).</summary>
    public static Granularidade? Fonte(Granularidade g) => g switch
    {
        Granularidade.Dia => Granularidade.Hora,
        Granularidade.Semana => Granularidade.Dia,
        Granularidade.Mes => Granularidade.Dia,
        Granularidade.Ano => Granularidade.Mes,
        _ => null
    };

    public static Granularidade? DeTexto(string? texto) => texto?.Trim().ToLowerInvariant() switch
    {
        "hora" => Granularidade.Hora,
        "dia" => Granularidade.Dia,
        "semana" => Granularidade.Semana,
        "mes" or "mês" => Granularidade.Mes,
        "ano" => Granularidade.Ano,
        _ => null
    };

    public static string Texto(Granularidade g) => g switch
    {
        Granularidade.Hora => "hora",
        Granularidade.Dia => "dia",
        Granularidade.Semana => "semana",
        Granularidade.Mes => "mes",
        _ => "ano"
    };

    /// <summary>O MySQL devolve DATETIME sem fuso; aqui todo horário gravado é UTC.</summary>
    public static DateTime Utc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc);

    /// <summary>DATETIME(3) guarda milissegundos: corta o resto para comparar memória e banco.</summary>
    public static DateTime TruncarMs(DateTime d) =>
        new(d.Ticks - d.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
}
