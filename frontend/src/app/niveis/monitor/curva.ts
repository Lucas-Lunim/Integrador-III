import { AfterViewInit, Component, DestroyRef, ElementRef, effect, inject, input, signal, viewChild } from '@angular/core';
import { numero, horas } from '../../dados/formatos';
import { Amostra, Intervalo, dataHora, horario } from './modelo';

interface Faixa {
  x: number;
  largura: number;
  anterior: number;
  retorno: number;
}

@Component({
  selector: 'app-curva',
  template: `
    <canvas #area role="img" [attr.aria-label]="rotulo()" [attr.tabindex]="grande() ? 0 : null"
      [attr.aria-describedby]="grande() ? 'chart-guide' : null"
      (pointermove)="explorar($event)" (pointerleave)="dica.set(null)" (pointerdown)="explorar($event)"
      (keydown)="navegar($event)" (blur)="dica.set(null)"></canvas>
    @if (dica(); as leitura) {
      <output class="chart-tooltip" [style.left.px]="leitura.x" style="top: 12px">
        <strong>{{ leitura.titulo }}</strong>
        <span>{{ leitura.texto }}</span>
      </output>
    }
  `,
})
export class Curva implements AfterViewInit {
  private readonly elemento = viewChild.required<ElementRef<HTMLCanvasElement>>('area');
  private readonly destroy = inject(DestroyRef);
  readonly dados = input.required<readonly Amostra[]>();
  readonly escala = input.required<readonly [number, number]>();
  readonly rotulo = input.required<string>();
  readonly unidade = input<string | null>(null);
  readonly tolerancia = input.required<number>();
  readonly grande = input(false);
  readonly ocorrencia = input<Intervalo | null>(null);
  protected readonly dica = signal<{ x: number; titulo: string; texto: string } | null>(null);
  private readonly dimensoes = signal({ largura: 0, altura: 0 });
  private pontos: number[] = [];
  private faixas: Faixa[] = [];
  private cursor = -1;

  constructor() {
    effect(() => {
      const dimensoes = this.dimensoes();
      const dados = this.dados();
      const escala = this.escala();
      const tolerancia = this.tolerancia();
      const grande = this.grande();
      const ocorrencia = this.ocorrencia();
      if (dimensoes.largura > 60 && dimensoes.altura > 20) {
        this.desenhar(dimensoes.largura, dimensoes.altura, dados, escala, tolerancia, grande, ocorrencia);
      }
    });
  }

  ngAfterViewInit() {
    const canvas = this.elemento().nativeElement;
    const observer = new ResizeObserver(() => {
      this.dimensoes.set({ largura: canvas.clientWidth, altura: canvas.clientHeight });
    });
    observer.observe(canvas);
    this.destroy.onDestroy(() => observer.disconnect());
  }

  protected explorar(evento: PointerEvent) {
    if (!this.grande() || !this.pontos.length) return;
    const x = evento.clientX - this.elemento().nativeElement.getBoundingClientRect().left;
    const faixa = this.faixas.find((f) => f.x <= x && x <= f.x + f.largura);
    if (faixa) {
      this.mostrar(x, `Lacuna de ${horas((faixa.retorno - faixa.anterior) / 3_600_000)}`,
        `${dataHora.format(faixa.anterior)} a ${dataHora.format(faixa.retorno)}`);
      return;
    }
    let esquerda = 0;
    let direita = this.pontos.length - 1;
    while (esquerda < direita) {
      const meio = (esquerda + direita) >>> 1;
      if (this.pontos[meio] < x) esquerda = meio + 1;
      else direita = meio;
    }
    this.cursor = esquerda > 0 && x - this.pontos[esquerda - 1] < this.pontos[esquerda] - x ? esquerda - 1 : esquerda;
    this.mostrarAmostra();
  }

  protected navegar(evento: KeyboardEvent) {
    if (!this.grande() || !this.pontos.length || !['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(evento.key)) return;
    evento.preventDefault();
    if (evento.key === 'Home' || evento.key === 'End') {
      this.cursor = evento.key === 'Home' ? 0 : this.pontos.length - 1;
    } else {
      const passo = evento.key === 'ArrowLeft' ? -1 : 1;
      this.cursor = Math.max(0, Math.min(this.pontos.length - 1, this.cursor + passo));
    }
    this.mostrarAmostra();
  }

  private mostrarAmostra() {
    const ponto = this.dados()[this.cursor];
    if (!ponto) return;
    const unidade = this.unidade();
    this.mostrar(this.pontos[this.cursor], dataHora.format(ponto.t), numero(ponto.valor) + (unidade ? ` ${unidade}` : ''));
  }

  private mostrar(x: number, titulo: string, texto: string) {
    this.dica.set({ x: Math.max(8, Math.min(x + 12, this.dimensoes().largura - 165)), titulo, texto });
  }

  private desenhar(largura: number, altura: number, dados: readonly Amostra[], [min, max]: readonly [number, number],
    tolerancia: number, grande: boolean, ocorrencia: Intervalo | null) {
    const canvas = this.elemento().nativeElement;
    const densidade = devicePixelRatio || 1;
    canvas.width = largura * densidade;
    canvas.height = altura * densidade;
    const ctx = canvas.getContext('2d');
    this.pontos = [];
    this.faixas = [];
    if (!ctx || !dados.length) return;
    ctx.scale(densidade, densidade);

    const esquerda = grande ? 49 : 31;
    const direita = largura - 7;
    const topo = 8;
    const fundo = altura - (grande ? 25 : 15);
    const espaco = direita - esquerda;
    const deltas = dados.slice(1).map(({ t }, i) => t - dados[i].t);
    const totalLacunas = deltas.filter((d) => d > tolerancia).length;
    const duracaoNormal = deltas.reduce((soma, d) => soma + (d > tolerancia ? 0 : d), 0);
    const separacao = totalLacunas ? Math.min(9, espaco * 0.24 / totalLacunas) : 0;
    const escalaTempo = duracaoNormal ? (espaco - totalLacunas * separacao) / duracaoNormal : 0;
    const y = (valor: number) => fundo - (valor - min) / (max - min) * (fundo - topo);
    let x = esquerda + (duracaoNormal ? 0 : (espaco - totalLacunas * separacao) / 2);
    this.pontos.push(x);

    for (const [i, delta] of deltas.entries()) {
      if (delta > tolerancia) {
        this.faixas.push({ x, largura: separacao, anterior: dados[i].t, retorno: dados[i + 1].t });
        x += separacao;
      } else {
        x += delta * escalaTempo;
      }
      this.pontos.push(x);
    }

    if (ocorrencia) {
      const inicio = dados.findIndex(({ t }) => t >= ocorrencia[0]);
      if (inicio >= 0 && dados[inicio].t <= ocorrencia[1]) {
        let fim = inicio;
        while (fim + 1 < dados.length && dados[fim + 1].t <= ocorrencia[1]) fim++;
        ctx.fillStyle = '#faebbf';
        ctx.fillRect(this.pontos[inicio] - 2, topo, this.pontos[fim] - this.pontos[inicio] + 4, fundo - topo);
      }
    }
    ctx.fillStyle = '#d5e0e4';
    for (const faixa of this.faixas) ctx.fillRect(faixa.x, topo, faixa.largura, fundo - topo);
    ctx.strokeStyle = '#d3e1e5';
    ctx.fillStyle = '#385560';
    ctx.font = `${grande ? 11 : 9}px Segoe UI, sans-serif`;
    ctx.textAlign = 'right';
    for (const valor of [min, (min + max) / 2, max]) {
      ctx.beginPath();
      ctx.moveTo(esquerda, y(valor));
      ctx.lineTo(direita, y(valor));
      ctx.stroke();
      ctx.fillText(numero(valor), esquerda - 4, y(valor) + 3, esquerda - 5);
    }

    ctx.strokeStyle = '#087f83';
    ctx.lineWidth = grande ? 1.7 : 1.35;
    ctx.beginPath();
    ctx.moveTo(this.pontos[0], y(dados[0].valor));
    for (const [i, amostra] of dados.entries()) {
      if (!i) continue;
      const yy = y(amostra.valor);
      if (deltas[i - 1] > tolerancia) ctx.moveTo(this.pontos[i], yy);
      else ctx.lineTo(this.pontos[i], yy);
    }
    ctx.stroke();

    ctx.strokeStyle = '#637f84';
    ctx.lineWidth = 1;
    ctx.setLineDash([2, 2]);
    ctx.beginPath();
    for (const [i, delta] of deltas.entries()) {
      if (delta <= tolerancia) continue;
      ctx.moveTo(this.pontos[i], y(dados[i].valor));
      ctx.lineTo(this.pontos[i + 1], y(dados[i + 1].valor));
    }
    ctx.stroke();
    ctx.setLineDash([]);

    ctx.fillStyle = '#087f83';
    if (dados.length > 1) {
      for (const [i, delta] of deltas.entries()) {
        if (delta <= tolerancia || (i > 0 && deltas[i - 1] <= tolerancia)) continue;
        ctx.fillRect(this.pontos[i] - 1, y(dados[i].valor) - 1, 2, 2);
      }
    }
    ctx.beginPath();
    ctx.arc(this.pontos.at(-1)!, y(dados.at(-1)!.valor), 2.7, 0, Math.PI * 2);
    ctx.fill();
    ctx.fillStyle = '#385560';
    const diaDiferente = dados.at(-1)!.t - dados[0].t >= 12 * 60 * 60_000;
    const formatar = (t: number) => diaDiferente ? dataHora.format(t) : horario.format(t);
    ctx.textAlign = 'left';
    ctx.fillText(formatar(dados[0].t), esquerda, altura - 2, espaco * 0.46);
    if (dados.length > 1) {
      ctx.textAlign = 'right';
      ctx.fillText(formatar(dados.at(-1)!.t), direita, altura - 2, espaco * 0.46);
    }
  }
}
