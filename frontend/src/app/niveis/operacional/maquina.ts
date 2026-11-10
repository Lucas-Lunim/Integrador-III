import { Component, DestroyRef, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api, Leitura, Serie } from '../../dados/api';
import { Catalogo } from '../../dados/catalogo';
import { desde, haQuanto, hora, horaCompleta, numero } from '../../dados/formatos';
import { Relogio } from '../../dados/relogio';
import { TempoReal } from '../../dados/tempo-real';
import { StatusMaquina } from '../../componentes/status';
import { Grafico, OpcoesGrafico } from '../../graficos/grafico';
import { corSerie, eixoValor, FONTE, lerTema, tooltipEixo } from '../../graficos/tema';

/** Intervalo acima do qual a linha é interrompida (mesmo limite de lacuna do backend). */
const LACUNA_MS = 60_000;

interface Trecho {
  esquerda: number;
  largura: number;
  classe: string;
  titulo: string;
}

@Component({
  selector: 'app-maquina',
  imports: [RouterLink, StatusMaquina, Grafico],
  templateUrl: './maquina.html',
  styleUrl: './maquina.css',
})
export default class Maquina {
  /** Código da máquina, vindo da rota /operacional/:maquina. */
  readonly maquina = input.required<string>();

  private readonly api = inject(Api);
  protected readonly tempoReal = inject(TempoReal);
  protected readonly catalogo = inject(Catalogo);
  private readonly relogio = inject(Relogio);
  protected readonly agora = this.relogio.agora;

  protected readonly janelas = [15, 30, 60];
  protected readonly janela = signal(60);
  protected readonly serie = signal<Serie | null>(null);
  protected readonly erro = signal<string | null>(null);

  constructor() {
    this.tempoReal.iniciar();
    const destruir = inject(DestroyRef);

    effect(() => {
      const maquina = this.maquina();
      const janela = this.janela();
      untracked(() => this.carregar(maquina, janela));
    });

    const assinatura = this.tempoReal.leituras.subscribe((l) => this.acrescentar(l));
    // Sem o canal ao vivo, recarrega o histórico periodicamente.
    const recarga = setInterval(() => {
      if (!this.tempoReal.conectado()) this.carregar(this.maquina(), this.janela());
    }, 15_000);
    destruir.onDestroy(() => {
      assinatura.unsubscribe();
      clearInterval(recarga);
    });
  }

  protected readonly atual = computed(
    () => this.tempoReal.agora()?.maquinas.find((m) => m.codigo === this.maquina()) ?? null,
  );

  /** Faixa de status da janela: trechos contínuos de mesmo status, e "sem dados" nas lacunas. */
  protected readonly faixa = computed<Trecho[]>(() => {
    const s = this.serie();
    const fim = this.agora();
    const inicio = fim - this.janela() * 60_000;
    const total = fim - inicio;
    if (!s || s.instantes.length === 0) return [];

    const trechos: { de: number; ate: number; status: string }[] = [];
    const empurrar = (de: number, ate: number, status: string) => {
      de = Math.max(de, inicio);
      if (ate <= de) return;
      const ultimo = trechos.at(-1);
      if (ultimo && ultimo.status === status && Math.abs(ultimo.ate - de) < 1) ultimo.ate = ate;
      else trechos.push({ de, ate, status });
    };
    empurrar(inicio, s.instantes[0], '');
    for (let i = 0; i < s.instantes.length; i++) {
      const t = s.instantes[i];
      const proximo = i + 1 < s.instantes.length ? s.instantes[i + 1] : fim;
      const ate = Math.min(proximo, t + LACUNA_MS);
      empurrar(t, ate, s.status[i]);
      if (proximo > ate) empurrar(ate, proximo, '');
    }

    return trechos.map((x) => {
      const categoria = x.status ? this.catalogo.categoriaStatus(x.status) : null;
      const classe = !categoria
        ? 'sem-dados'
        : categoria === 'OPERACAO' && this.catalogo.alertaStatus(x.status)
          ? 'atencao'
          : categoria.toLowerCase();
      const nome = x.status ? this.catalogo.nomeStatus(x.status) : 'Sem dados';
      return {
        esquerda: ((x.de - inicio) / total) * 100,
        largura: ((x.ate - x.de) / total) * 100,
        classe,
        titulo: `${nome}: ${horaCompleta(x.de)} a ${horaCompleta(x.ate)}`,
      };
    });
  });

  /** Um gráfico pequeno por atributo (small multiples): uma escala por medida, nunca dois eixos. */
  protected readonly graficos = computed(() => {
    const s = this.serie();
    if (!s) return [];
    const tema = lerTema();
    const cor = corSerie(tema, this.catalogo.indiceCor(s.maquina));
    const fim = Date.now();
    const inicio = fim - this.janela() * 60_000;
    const atributos = [...s.atributos].sort(
      (a, b) => Number(this.catalogo.ehContador(a)) - Number(this.catalogo.ehContador(b)),
    );

    return atributos.map((codigo) => {
      const valores = s.series[codigo] ?? [];
      const unidade = this.catalogo.unidade(codigo);
      const contador = this.catalogo.ehContador(codigo);
      const pontos: [number, number | null][] = [];
      let anterior = -Infinity;
      s.instantes.forEach((t, i) => {
        if (t - anterior > LACUNA_MS && pontos.length) pontos.push([anterior + 1, null]);
        pontos.push([t, valores[i]]);
        anterior = t;
      });
      const numericos = valores.filter((v): v is number => v != null);
      const ultimo = numericos.at(-1) ?? null;
      const formatar = (v: number | null) => (v == null ? '—' : `${numero(v, 3)}${unidade ? ' ' + unidade : ''}`);

      const opcoes: OpcoesGrafico = {
        animation: false,
        grid: { left: 52, right: 14, top: 10, bottom: 24 },
        tooltip: {
          ...tooltipEixo(tema),
          formatter: (p: unknown) => {
            const ponto = (p as { value: [number, number | null] }[])[0];
            return ponto ? `${horaCompleta(ponto.value[0])}<br><b>${formatar(ponto.value[1])}</b>` : '';
          },
        },
        xAxis: {
          type: 'time',
          min: inicio,
          max: fim,
          axisLine: { lineStyle: { color: tema.eixo } },
          axisTick: { show: false },
          splitLine: { show: false },
          axisLabel: { color: tema.apagado, fontFamily: FONTE, fontSize: 11, hideOverlap: true, formatter: (v: number) => hora(v) },
        },
        yAxis: eixoValor(tema, (v) => numero(v, 2)),
        series: [
          {
            type: 'line',
            data: pontos,
            showSymbol: false,
            connectNulls: false,
            lineStyle: { width: 2, color: cor },
            itemStyle: { color: cor },
            emphasis: { disabled: true },
          },
        ],
      };
      return {
        codigo,
        nome: this.catalogo.nomeAtributo(codigo),
        rotulo: this.catalogo.rotuloAtributo(codigo),
        contador,
        ultimo,
        minimo: numericos.length ? Math.min(...numericos) : null,
        media: numericos.length ? numericos.reduce((a, b) => a + b, 0) / numericos.length : null,
        maximo: numericos.length ? Math.max(...numericos) : null,
        variacao: contador ? this.variacao(numericos) : null,
        opcoes,
      };
    });
  });

  protected readonly haQuanto = haQuanto;
  protected readonly desde = desde;
  protected readonly numero = numero;

  private carregar(maquina: string, janela: number) {
    this.api.serie(maquina, janela).subscribe({
      next: (s) => {
        this.serie.set(s);
        this.erro.set(null);
      },
      error: (e) => this.erro.set(e?.status === 404 ? 'Máquina não cadastrada.' : 'Não foi possível carregar o histórico.'),
    });
  }

  /** Acrescenta as leituras novas desta máquina ao fim das colunas e descarta o que saiu da janela. */
  private acrescentar(leituras: Leitura[]) {
    const s = this.serie();
    if (!s) return;
    const minhas = leituras.filter((l) => l.maquina === s.maquina);
    if (!minhas.length) return;

    const limite = Date.now() - this.janela() * 60_000;
    let corte = s.instantes.findIndex((t) => t >= limite);
    if (corte < 0) corte = s.instantes.length;
    const instantes = s.instantes.slice(corte);
    const status = s.status.slice(corte);
    const series: Record<string, (number | null)[]> = {};
    for (const a of s.atributos) series[a] = (s.series[a] ?? []).slice(corte);

    for (const l of minhas) {
      const t = Date.parse(l.instante);
      if (instantes.length && t <= instantes[instantes.length - 1]) continue;
      instantes.push(t);
      status.push(l.status);
      for (const a of s.atributos) series[a].push(l.valores[a] ?? null);
    }
    this.serie.set({ ...s, instantes, status, series });
  }

  /** Quanto o contador subiu na janela, ignorando quedas (reinício do contador). */
  private variacao(valores: number[]): number | null {
    if (valores.length < 2) return null;
    let soma = 0;
    for (let i = 1; i < valores.length; i++) soma += Math.max(0, valores[i] - valores[i - 1]);
    return soma;
  }
}
