using Pi3.Backend.Dados;

namespace Pi3.Backend.Sistemas;

/// <summary>Dados compartilhados em memória: cadastros (trocados inteiros) e estado ao vivo.</summary>
public sealed class Memoria(Configuracao cfg)
{
    volatile Dimensoes? dimensoes;
    readonly TaskCompletionSource pronta = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Histórico em anel: a API gera uma leitura a cada ~5 s por máquina; com
    /// folga de 25%, cabem <see cref="Configuracao.MinutosAoVivo"/> minutos.
    /// </summary>
    public EstadoAoVivo Estado { get; } = new(Math.Max(64, cfg.MinutosAoVivo * 60 / 4));

    public bool Pronta => dimensoes is not null;

    public Dimensoes Dimensoes => dimensoes ?? throw new InvalidOperationException("Cadastros ainda não carregados.");

    public void TrocarDimensoes(Dimensoes d)
    {
        dimensoes = d;
        pronta.TrySetResult();
    }

    public Task AguardarAsync(CancellationToken ct) => pronta.Task.WaitAsync(ct);
}
