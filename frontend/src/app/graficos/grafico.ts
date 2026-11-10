import { Component, DestroyRef, ElementRef, afterNextRender, effect, inject, input, viewChild } from '@angular/core';
import * as echarts from 'echarts/core';
import { BarChart, LineChart } from 'echarts/charts';
import { GridComponent, LegendComponent, MarkLineComponent, TooltipComponent } from 'echarts/components';
import { CanvasRenderer } from 'echarts/renderers';

// Só os pedaços usados do ECharts entram no pacote.
echarts.use([LineChart, BarChart, GridComponent, LegendComponent, TooltipComponent, MarkLineComponent, CanvasRenderer]);

export type OpcoesGrafico = echarts.EChartsCoreOption;

/** Envolve um gráfico ECharts: recebe as opções prontas e redesenha quando elas mudam. */
@Component({
  selector: 'app-grafico',
  template: `<div #alvo class="alvo" [style.height.px]="altura()" role="img" [attr.aria-label]="descricao()"></div>`,
  styles: `.alvo { width: 100%; }`,
})
export class Grafico {
  readonly opcoes = input.required<OpcoesGrafico>();
  readonly altura = input(240);
  readonly descricao = input('');

  private readonly alvo = viewChild.required<ElementRef<HTMLDivElement>>('alvo');
  private readonly destruir = inject(DestroyRef);
  private instancia?: echarts.ECharts;

  constructor() {
    afterNextRender(() => {
      const elemento = this.alvo().nativeElement;
      this.instancia = echarts.init(elemento, undefined, { renderer: 'canvas' });
      this.instancia.setOption(this.opcoes(), { notMerge: true });
      const observador = new ResizeObserver(() => this.instancia?.resize());
      observador.observe(elemento);
      this.destruir.onDestroy(() => {
        observador.disconnect();
        this.instancia?.dispose();
      });
    });

    effect(() => {
      const opcoes = this.opcoes();
      this.instancia?.setOption(opcoes, { notMerge: true });
    });
  }
}
