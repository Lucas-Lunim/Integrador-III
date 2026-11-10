import { Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { forkJoin } from 'rxjs';
import { Api, Indicadores } from '../../dados/api';
import { Catalogo } from '../../dados/catalogo';
import { descricaoPeriodo, horas, inteiro, numero, percentual, rotuloPeriodo } from '../../dados/formatos';
import { celula, serieDoGrupo, variacao } from '../../dados/indicadores';
import { Grafico, OpcoesGrafico } from '../../graficos/grafico';
import { corSerie, eixoCategorias, eixoValor, legenda, lerTema, tooltipEixo } from '../../graficos/tema';

type Granularidade = 'semana' | 'mes';

@Component({
  selector: 'app-tatico',
  imports: [Grafico],
  templateUrl: './tatico.html',
  styleUrl: './tatico.css',
})
export default class Tatico {
  private readonly api = inject(Api);
  protected readonly catalogo = inject(Catalogo);

  protected readonly granularidade = signal<Granularidade>('semana');
  protected readonly maquina = signal('');
  protected readonly porMaquina = signal<Indicadores | null>(null);
  protected readonly planta = signal<Indicadores | null>(null);
  protected readonly erro = signal<string | null>(null);
  /** Período escolhido na lista; null = o mais recente (em andamento). */
  protected readonly escolhido = signal<number | null>(null);

  constructor() {
    effect(() => {
      const g = this.granularidade();
      const m = this.maquina();
      untracked(() => {
        this.escolhido.set(null);
        this.carregar(g, m);
      });
    });
    // Resumos mudam devagar: recarregar a cada minuto mantém o período corrente em dia.
    const recarga = setInterval(() => this.carregar(this.granularidade(), this.maquina()), 60_000);
    inject(DestroyRef).onDestroy(() => clearInterval(recarga));
  }

  protected readonly periodo = computed(() => {
    const ind = this.planta();
    return this.escolhido() ?? (ind ? ind.periodos.length - 1 : 0);
  });

  protected readonly rotulos = computed(() => {
    const ind = this.planta();
    return ind ? ind.periodos.map((p) => rotuloPeriodo(p, ind.granularidade)) : [];
  });

  protected readonly titulo = computed(() => {
    const ind = this.planta();
    const p = this.periodo();
    return ind ? descricaoPeriodo(ind.periodos[p], ind.fimPeriodos[p], ind.granularidade) : '';
  });

  /** Indicadores do período escolhido, para todas as máquinas do filtro somadas. */
  protected readonly resumo = computed(() => {
    const ind = this.planta();
    const p = this.periodo();
    const v = (c: string, periodo = p) => celula(ind, c, periodo, 0);
    return {
      producao: v('producao'),
      producaoVariacao: p > 0 ? variacao(v('producao'), v('producao', p - 1)) : null,
      taxaPerdas: v('taxaPerdas'),
      perdas: v('perdas'),
      disponibilidade: v('disponibilidade'),
      horasParada: v('horasParada'),
      horasManutencao: v('horasManutencao'),
      cobertura: v('cobertura'),
    };
  });

  /** Uma linha por máquina no período escolhido (a mesma informação dos gráficos, em tabela). */
  protected readonly linhas = computed(() => {
    const ind = this.porMaquina();
    if (!ind) return [];
    const p = this.periodo();
    return ind.grupos.map((g, i) => {
      const v = (c: string) => celula(ind, c, p, i);
      return {
        codigo: g.codigo,
        nome: this.catalogo.nomeMaquina(g.codigo),
        cor: this.catalogo.corMaquina(g.codigo),
        producao: v('producao') ?? v('ciclos'),
        unidadeProducao: v('producao') != null ? 'peças' : v('ciclos') != null ? 'ciclos' : '',
        perdas: v('perdas'),
        taxaPerdas: v('taxaPerdas'),
        disponibilidade: v('disponibilidade'),
        horasOperacao: v('horasOperacao'),
        horasManutencao: v('horasManutencao'),
        horasParada: v('horasParada'),
        temperaturaMedia: v('temperaturaMedia'),
        temperaturaMax: v('temperaturaMax'),
        vibracaoMedia: v('vibracaoMedia'),
        desgaste: v('desgaste'),
        cobertura: v('cobertura'),
      };
    });
  });

  protected readonly graficoProducao = computed<OpcoesGrafico | null>(() => {
    const ind = this.porMaquina();
    if (!ind) return null;
    const tema = lerTema();
    const series = ind.grupos
      .map((g, i) => ({ g, dados: serieDoGrupo(ind, 'producao', i) }))
      .filter((s) => s.dados.some((v) => v != null))
      .map(({ g, dados }) => {
        const cor = corSerie(tema, this.catalogo.indiceCor(g.codigo));
        return {
          name: this.catalogo.nomeMaquina(g.codigo),
          type: 'bar',
          data: dados,
          barMaxWidth: 18,
          itemStyle: { color: cor, borderRadius: [4, 4, 0, 0] },
          emphasis: { focus: 'series' },
        };
      });
    if (!series.length) return null;
    return {
      animation: false,
      grid: { left: 56, right: 12, top: 40, bottom: 28 },
      legend: legenda(tema),
      tooltip: { ...tooltipEixo(tema, (v) => inteiro(v)), axisPointer: { type: 'shadow' } },
      xAxis: eixoCategorias(tema, this.rotulos()),
      yAxis: { ...eixoValor(tema, (v) => inteiro(v)), scale: false },
      series,
    };
  });

  protected readonly graficoDisponibilidade = computed<OpcoesGrafico | null>(() => {
    const ind = this.porMaquina();
    if (!ind) return null;
    const tema = lerTema();
    const series = ind.grupos
      .map((g, i) => ({ g, dados: serieDoGrupo(ind, 'disponibilidade', i) }))
      .filter((s) => s.dados.some((v) => v != null))
      .map(({ g, dados }) => {
        const cor = corSerie(tema, this.catalogo.indiceCor(g.codigo));
        return {
          name: this.catalogo.nomeMaquina(g.codigo),
          type: 'line',
          data: dados,
          symbol: 'circle',
          symbolSize: 8,
          connectNulls: false,
          lineStyle: { width: 2, color: cor },
          itemStyle: { color: cor, borderColor: tema.superficie, borderWidth: 2 },
        };
      });
    if (!series.length) return null;
    return {
      animation: false,
      grid: { left: 52, right: 12, top: 40, bottom: 28 },
      legend: legenda(tema),
      tooltip: tooltipEixo(tema, (v) => percentual(v)),
      xAxis: { ...eixoCategorias(tema, this.rotulos()), boundaryGap: false },
      yAxis: { ...eixoValor(tema, (v) => percentual(v, 0)), scale: false, min: 0, max: 1 },
      series,
    };
  });

  protected readonly opcoesMaquinas = computed(() => this.catalogo.dimensoes()?.maquinas.map((m) => m.codigo) ?? []);

  protected readonly numero = numero;
  protected readonly inteiro = inteiro;
  protected readonly percentual = percentual;
  protected readonly horas = horas;

  protected trocarMaquina(evento: Event) {
    this.maquina.set((evento.target as HTMLSelectElement).value);
  }

  private carregar(g: Granularidade, maquina: string) {
    const quantidade = g === 'semana' ? 8 : 6;
    forkJoin([
      this.api.tatico(g, quantidade, 'maquina', maquina || undefined),
      this.api.tatico(g, quantidade, 'planta', maquina || undefined),
    ]).subscribe({
      next: ([porMaquina, planta]) => {
        this.porMaquina.set(porMaquina);
        this.planta.set(planta);
        this.erro.set(null);
      },
      error: () => this.erro.set('Não foi possível carregar os resumos. O backend está no ar?'),
    });
  }
}
