import { Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { Subscription, forkJoin } from 'rxjs';
import { Paginacao, Paginador, TamanhoPaginaControle } from '../../componentes/paginacao';
import { Api, Indicadores } from '../../dados/api';
import { descricaoPeriodo, horas, inteiro, numero, percentual, rotuloPeriodo } from '../../dados/formatos';
import { celula, serieDoGrupo, variacao } from '../../dados/indicadores';
import { Grafico, OpcoesGrafico } from '../../graficos/grafico';
import { eixoCategorias, eixoValor, lerTema, Tema, tooltipEixo } from '../../graficos/tema';

type Granularidade = 'mes' | 'ano';

@Component({
  selector: 'app-estrategico',
  imports: [Grafico, TamanhoPaginaControle, Paginador],
  templateUrl: './estrategico.html',
  styleUrl: './estrategico.css',
})
export default class Estrategico {
  private readonly api = inject(Api);
  private pedido?: Subscription;

  protected readonly granularidade = signal<Granularidade>('mes');
  protected readonly planta = signal<Indicadores | null>(null);
  protected readonly porTipo = signal<Indicadores | null>(null);
  protected readonly erro = signal<string | null>(null);
  protected readonly escolhido = signal<number | null>(null);

  constructor() {
    effect(() => {
      const g = this.granularidade();
      untracked(() => {
        this.escolhido.set(null);
        this.paginacao.reiniciar();
        this.carregar(g);
      });
    });
    const recarga = setInterval(() => this.carregar(this.granularidade()), 5 * 60_000);
    inject(DestroyRef).onDestroy(() => {
      clearInterval(recarga);
      this.pedido?.unsubscribe();
    });
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

  protected readonly resumo = computed(() => {
    const ind = this.planta();
    const p = this.periodo();
    const v = (c: string, periodo = p) => celula(ind, c, periodo, 0);
    const parado = (periodo: number) => {
      const m = v('horasManutencao', periodo);
      const pa = v('horasParada', periodo);
      return m == null && pa == null ? null : (m ?? 0) + (pa ?? 0);
    };
    return {
      producao: v('producao'),
      producaoVariacao: p > 0 ? variacao(v('producao'), v('producao', p - 1)) : null,
      taxaPerdas: v('taxaPerdas'),
      disponibilidade: v('disponibilidade'),
      falhas: v('falhas'),
      horasSemOperar: parado(p),
      horasObservadas: v('horasObservadas'),
      cobertura: v('cobertura'),
    };
  });

  protected readonly tipos = computed(() => {
    const ind = this.porTipo();
    if (!ind) return [];
    const p = this.periodo();
    return ind.grupos.map((g, i) => {
      const v = (c: string) => celula(ind, c, p, i);
      return {
        codigo: g.codigo,
        nome: g.nome,
        producao: v('producao') ?? v('ciclos'),
        unidadeProducao: v('producao') != null ? 'peças' : v('ciclos') != null ? 'ciclos' : '',
        taxaPerdas: v('taxaPerdas'),
        disponibilidade: v('disponibilidade'),
        horasObservadas: v('horasObservadas'),
        horasSemOperar: (v('horasManutencao') ?? 0) + (v('horasParada') ?? 0),
        temperaturaMedia: v('temperaturaMedia'),
        falhas: v('falhas'),
      };
    });
  });

  protected readonly paginacao = new Paginacao(() => this.tipos().length);

  // Uma medida por gráfico (produção em peças, disponibilidade e perdas em %): nunca dois eixos.
  protected readonly graficoProducao = computed(() =>
    this.grafico('producao', 'bar', (v) => inteiro(v), (v) => inteiro(v), false),
  );
  protected readonly graficoDisponibilidade = computed(() =>
    this.grafico('disponibilidade', 'line', (v) => percentual(v), (v) => percentual(v, 0), true),
  );
  protected readonly graficoPerdas = computed(() =>
    this.grafico('taxaPerdas', 'line', (v) => percentual(v, 2), (v) => percentual(v, 2), true),
  );

  protected readonly numero = numero;
  protected readonly inteiro = inteiro;
  protected readonly percentual = percentual;
  protected readonly horas = horas;

  private grafico(
    coluna: string,
    forma: 'bar' | 'line',
    dica: (v: number | null) => string,
    eixo: (v: number) => string,
    zeroAteUm: boolean,
  ): OpcoesGrafico | null {
    const ind = this.planta();
    if (!ind) return null;
    const dados = serieDoGrupo(ind, coluna, 0);
    if (!dados.some((v) => v != null)) return null;
    const tema: Tema = lerTema();
    const cor = tema.series[0];
    return {
      animation: false,
      grid: { left: 56, right: 12, top: 14, bottom: 28 },
      tooltip: { ...tooltipEixo(tema, dica), axisPointer: { type: forma === 'bar' ? 'shadow' : 'line' } },
      xAxis: { ...eixoCategorias(tema, this.rotulos()), boundaryGap: forma === 'bar' },
      yAxis: { ...eixoValor(tema, eixo), scale: false, ...(zeroAteUm ? { min: 0, max: coluna === 'taxaPerdas' ? undefined : 1 } : {}) },
      series: [
        forma === 'bar'
          ? { type: 'bar', data: dados, barMaxWidth: 28, itemStyle: { color: cor, borderRadius: [4, 4, 0, 0] } }
          : {
              type: 'line',
              data: dados,
              symbol: 'circle',
              symbolSize: 8,
              connectNulls: false,
              lineStyle: { width: 2, color: cor },
              itemStyle: { color: cor, borderColor: tema.superficie, borderWidth: 2 },
            },
      ],
    };
  }

  private carregar(g: Granularidade) {
    const quantidade = g === 'mes' ? 12 : 5;
    this.pedido?.unsubscribe();
    this.pedido = forkJoin([this.api.estrategico(g, quantidade, 'planta'), this.api.estrategico(g, quantidade, 'tipo')]).subscribe({
      next: ([planta, porTipo]) => {
        this.planta.set(planta);
        this.porTipo.set(porTipo);
        this.erro.set(null);
      },
      error: () => this.erro.set('Não foi possível carregar os resumos. O backend está no ar?'),
    });
  }
}
