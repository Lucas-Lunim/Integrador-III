export const FONTE = 'system-ui, -apple-system, "Segoe UI", Roboto, sans-serif';

export interface Tema {
  tinta: string;
  tinta2: string;
  apagado: string;
  grade: string;
  eixo: string;
  superficie: string;
  series: string[];
}

/** Cores do painel, compartilhadas entre os gráficos e as tabelas. */
export function lerTema(): Tema {
  const css = getComputedStyle(document.querySelector('app-root') ?? document.documentElement);
  const v = (nome: string) => css.getPropertyValue(nome).trim();
  return {
    tinta: v('--tinta'),
    tinta2: v('--tinta-2'),
    apagado: v('--apagado'),
    grade: v('--grade'),
    eixo: v('--eixo'),
    superficie: v('--superficie'),
    series: [1, 2, 3, 4, 5, 6].map((i) => v(`--s${i}`)),
  };
}

export function corSerie(t: Tema, indice: number): string {
  return indice >= 0 && indice < t.series.length ? t.series[indice] : t.apagado;
}

/** Tooltip de eixo com linha-guia (crosshair), comum a linhas e barras. */
export function tooltipEixo(t: Tema, formatar?: (v: number | null) => string) {
  return {
    trigger: 'axis',
    confine: true,
    axisPointer: { type: 'line', lineStyle: { color: t.eixo, width: 1 } },
    backgroundColor: t.superficie,
    borderColor: t.eixo,
    textStyle: { color: t.tinta, fontFamily: FONTE, fontSize: 12 },
    valueFormatter: formatar ? (v: unknown) => formatar(typeof v === 'number' ? v : null) : undefined,
  };
}

/** Eixos recessivos: linha de base fina, grade em linha contínua clara, rótulos apagados. */
export function eixoValor(t: Tema, formatar?: (v: number) => string) {
  return {
    type: 'value',
    scale: true,
    axisLine: { show: false },
    axisTick: { show: false },
    splitLine: { lineStyle: { color: t.grade, width: 1 } },
    axisLabel: { color: t.apagado, fontFamily: FONTE, fontSize: 11, formatter: formatar },
  };
}

export function eixoCategorias(t: Tema, categorias: string[]) {
  return {
    type: 'category',
    data: categorias,
    axisLine: { lineStyle: { color: t.eixo } },
    axisTick: { show: false },
    axisLabel: { color: t.apagado, fontFamily: FONTE, fontSize: 11 },
  };
}

/** Legenda numa linha só (rola se não couber), para nunca invadir a área do gráfico. */
export function legenda(t: Tema) {
  return {
    type: 'scroll',
    top: 0,
    left: 0,
    right: 0,
    pageIconColor: t.tinta2,
    pageIconInactiveColor: t.eixo,
    pageTextStyle: { color: t.apagado },
    icon: 'roundRect',
    itemWidth: 12,
    itemHeight: 8,
    textStyle: { color: t.tinta2, fontFamily: FONTE, fontSize: 12 },
  };
}
