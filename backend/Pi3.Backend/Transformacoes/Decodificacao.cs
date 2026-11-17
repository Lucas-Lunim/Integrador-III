using System.Globalization;
using System.Text;
using System.Text.Json;
using Pi3.Backend.Dados;

namespace Pi3.Backend.Transformacoes;

/// <summary>JSON da API → <see cref="LoteRecebido"/> (colunas com códigos em texto).</summary>
public static class Decodificacao
{
    /// <summary>Campos que descrevem a leitura; todo o resto é tratado como atributo numérico.</summary>
    static readonly string[] CamposDaLeitura = ["id", "timestamp", "maquina", "tipo", "status"];

    public static LoteRecebido Ler(byte[] json, DateTime coletadoEm)
    {
        var lote = new LoteRecebido(coletadoEm);
        using var documento = JsonDocument.Parse(json);
        if (Lista(documento.RootElement) is not { } lista) return lote;

        foreach (var item in lista.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) { lote.LeiturasInvalidas++; continue; }

            string? maquina = null, tipo = null, status = null, timestamp = null;
            long id = -1;
            foreach (var campo in item.EnumerateObject())
            {
                switch (campo.Name)
                {
                    case "maquina": maquina = Texto(campo.Value); break;
                    case "tipo": tipo = Texto(campo.Value); break;
                    case "status": status = Texto(campo.Value); break;
                    case "timestamp": timestamp = Texto(campo.Value); break;
                    case "id" when campo.Value.ValueKind == JsonValueKind.Number && campo.Value.TryGetInt64(out var v): id = v; break;
                }
            }
            if (string.IsNullOrWhiteSpace(maquina)) { lote.LeiturasInvalidas++; continue; }

            var flags = Qualidade.Nenhuma;
            if (!TentarInstante(timestamp, coletadoEm, out var instante))
            {
                instante = Periodo.TruncarMs(coletadoEm);
                flags |= Qualidade.InstanteDaColeta;
            }
            var codigoStatus = NormalizarStatus(status);
            if (codigoStatus is null)
            {
                codigoStatus = "DESCONHECIDO";
                flags |= Qualidade.StatusDesconhecido;
            }

            var primeiro = lote.Valor.Count;
            foreach (var campo in item.EnumerateObject())
            {
                if (Array.IndexOf(CamposDaLeitura, campo.Name) >= 0) continue;
                if (TentarNumero(campo.Value, out var numero))
                {
                    lote.Atributo.Add(campo.Name.Trim());
                    lote.Valor.Add(numero);
                }
                else flags |= Qualidade.ValorInvalido;
            }

            lote.Instante.Add(instante);
            lote.Maquina.Add(maquina.Trim());
            lote.Tipo.Add(string.IsNullOrWhiteSpace(tipo) ? null : tipo.Trim());
            lote.Status.Add(codigoStatus);
            lote.IdApi.Add(id);
            lote.Flags.Add(flags);
            lote.PrimeiroValor.Add(primeiro);
            lote.QtdValores.Add(lote.Valor.Count - primeiro);
        }
        return lote;
    }

    /// <summary>A API responde {"total": n, "dados": [...]}; também aceita a lista pura.</summary>
    static JsonElement? Lista(JsonElement raiz)
    {
        if (raiz.ValueKind == JsonValueKind.Array) return raiz;
        if (raiz.ValueKind != JsonValueKind.Object) return null;
        if (raiz.TryGetProperty("dados", out var dados) && dados.ValueKind == JsonValueKind.Array) return dados;
        foreach (var campo in raiz.EnumerateObject())
            if (campo.Value.ValueKind == JsonValueKind.Array) return campo.Value;
        return null;
    }

    static string? Texto(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.GetRawText(),
        _ => null
    };

    /// <summary>T08 — aceita número JSON ou texto numérico (vírgula ou ponto). Nulo, booleano e texto livre são ignorados.</summary>
    static bool TentarNumero(JsonElement e, out double numero)
    {
        numero = double.NaN;
        if (e.ValueKind == JsonValueKind.Number) return e.TryGetDouble(out numero) && double.IsFinite(numero);
        if (e.ValueKind == JsonValueKind.String && e.GetString() is { } s)
            return double.TryParse(s.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out numero)
                && double.IsFinite(numero);
        return false;
    }

    /// <summary>
    /// T03 — o "timestamp" da API é a hora oficial da leitura. Ausente, inválido
    /// ou mais de um dia à frente do relógio do coletor (o que travaria a
    /// máquina, já que leituras mais antigas que a última são ignoradas) é recusado.
    /// </summary>
    static bool TentarInstante(string? texto, DateTime coletadoEm, out DateTime instante)
    {
        instante = default;
        if (string.IsNullOrWhiteSpace(texto)) return false;
        if (!DateTime.TryParse(texto, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var lido)) return false;
        if (lido.Year < 2000 || lido > coletadoEm.AddDays(1)) return false;
        instante = Periodo.TruncarMs(Periodo.Utc(lido));
        return true;
    }

    /// <summary>T07 — "Em manutenção", "em-manutencao" e "EM_MANUTENCAO" viram o mesmo código.</summary>
    public static string? NormalizarStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return null;
        var semAcento = new StringBuilder();
        foreach (var c in status.Trim().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            semAcento.Append(char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : '_');
        }
        var codigo = string.Join('_', semAcento.ToString().Split('_', StringSplitOptions.RemoveEmptyEntries));
        return codigo.Length == 0 ? null : codigo;
    }
}
