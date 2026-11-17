using System.Collections.Frozen;

namespace Pi3.Backend.Dados;

public enum Natureza : byte { Medida = 0, Contador = 1 }

public enum Categoria : byte { Operacao = 0, Manutencao = 1, Parada = 2, Desconhecido = 3 }

/// <summary>
/// Cadastros carregados do banco, em colunas indexadas pelo id (a posição 0
/// não é usada; um código vazio marca id inexistente). O objeto é imutável:
/// quando a API traz um código novo, o coletor grava no banco e troca o
/// objeto inteiro por um recarregado.
/// </summary>
public sealed class Dimensoes
{
    public required string[] SetorNome { get; init; }

    public required string[] TipoCodigo { get; init; }
    public required string[] TipoNome { get; init; }
    /// <summary>tipo_id → ids dos atributos que o tipo envia (atributo_tipo_maquina).</summary>
    public required int[][] AtributosDoTipo { get; init; }

    public required string[] MaquinaCodigo { get; init; }
    public required int[] MaquinaTipo { get; init; }
    public required int[] MaquinaSetor { get; init; }

    public required string[] AtributoCodigo { get; init; }
    public required string[] AtributoNome { get; init; }
    public required string?[] AtributoUnidade { get; init; }
    public required Natureza[] AtributoNatureza { get; init; }

    public required string[] StatusCodigo { get; init; }
    public required string[] StatusNome { get; init; }
    public required Categoria[] StatusCategoria { get; init; }
    public required bool[] StatusAlerta { get; init; }

    public required FrozenDictionary<string, int> TipoPorCodigo { get; init; }
    public required FrozenDictionary<string, int> MaquinaPorCodigo { get; init; }
    public required FrozenDictionary<string, int> AtributoPorCodigo { get; init; }
    public required FrozenDictionary<string, int> StatusPorCodigo { get; init; }

    public int CapacidadeMaquinas => MaquinaCodigo.Length;
    public int CapacidadeAtributos => AtributoCodigo.Length;
    public int CapacidadeStatus => StatusCodigo.Length;

    /// <summary>Id do status usado quando a leitura não traz status.</summary>
    public int StatusDesconhecido => StatusPorCodigo.GetValueOrDefault("DESCONHECIDO");

    /// <summary>Ids das máquinas cadastradas, em ordem de id.</summary>
    public int[] MaquinasMonitoradas() =>
        [.. Maquinas().Where(m => TipoCodigo[MaquinaTipo[m]] != "ROBO_SOLDAGEM")];

    public int[] Maquinas()
    {
        var ids = new List<int>();
        for (var i = 1; i < MaquinaCodigo.Length; i++)
            if (MaquinaCodigo[i].Length > 0) ids.Add(i);
        return [.. ids];
    }

    public int Atributo(string codigo) => AtributoPorCodigo.GetValueOrDefault(codigo, -1);
}
