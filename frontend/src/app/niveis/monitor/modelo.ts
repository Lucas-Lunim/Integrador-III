import { Catalogo } from '../../dados/catalogo';
import { Serie } from '../../dados/api';

export const minuto = 60_000;
export const periodos = [
  { id: 'live', nome: 'Ao vivo', ms: 30_000 },
  { id: '5', nome: 'Últimos 5 minutos', ms: 5 * minuto },
  { id: '720', nome: 'Últimas 12 horas', ms: 720 * minuto },
  { id: '1440', nome: 'Últimas 24 horas', ms: 1440 * minuto },
  { id: 'all', nome: 'Desde o começo', ms: Infinity },
] as const;

export type Periodo = (typeof periodos)[number];
export type Condicao = 'Normal' | 'Atenção' | 'Crítica' | 'Sem condição';
export type Gravidade = 'Atenção' | 'Crítica';
export type Intervalo = readonly [number, number];

export interface Registro {
  t: number;
  status: string;
  valores: Record<string, number | null>;
}

export interface Amostra {
  t: number;
  valor: number;
}

export interface Alerta {
  inicio: Registro;
  fim: Registro;
  condicao: Gravidade;
  quantidade: number;
  resultado: string;
}

export interface Lacuna {
  anterior: number;
  retorno: number | null;
}

export const dataHora = new Intl.DateTimeFormat('pt-BR', {
  timeZone: 'America/Sao_Paulo', dateStyle: 'short', timeStyle: 'medium',
});
export const horario = new Intl.DateTimeFormat('pt-BR', {
  timeZone: 'America/Sao_Paulo', timeStyle: 'medium',
});

export function registros(serie: Serie): Registro[] {
  const atributos = Object.entries(serie.series);
  return serie.instantes.map((t, i) => {
    const valores: Registro['valores'] = {};
    for (const [codigo, coluna] of atributos) valores[codigo] = coluna[i];
    return { t, status: serie.status[i], valores };
  });
}

export function desdeIndice(linhas: readonly Registro[], inicio: number): number {
  let esquerda = 0;
  let direita = linhas.length;
  while (esquerda < direita) {
    const meio = Math.floor((esquerda + direita) / 2);
    if (linhas[meio].t < inicio) esquerda = meio + 1;
    else direita = meio;
  }
  return esquerda;
}

export function juntar(antigas: readonly Registro[], novas: readonly Registro[]): readonly Registro[] {
  if (!novas.length) return antigas;
  if (!antigas.length) return novas;
  if (antigas.at(-1)!.t < novas[0].t) return [...antigas, ...novas];

  let i = desdeIndice(antigas, novas[0].t);
  let j = 0;
  const linhas = antigas.slice(0, i);
  while (i < antigas.length && j < novas.length) {
    const anterior = antigas[i];
    const nova = novas[j];
    if (anterior.t < nova.t) {
      linhas.push(anterior);
      i++;
    } else {
      linhas.push(nova);
      if (anterior.t === nova.t) i++;
      j++;
    }
  }
  return linhas.concat(antigas.slice(i), novas.slice(j));
}

export function condicao(estado: string, catalogo: Catalogo): Condicao {
  const categoria = catalogo.categoriaStatus(estado);
  if (estado === 'FALHA' || categoria === 'PARADA') return 'Crítica';
  if (estado === 'ATENCAO' || categoria === 'MANUTENCAO' || catalogo.alertaStatus(estado)) return 'Atenção';
  if (estado === 'OPERANDO' || categoria === 'OPERACAO') return 'Normal';
  return 'Sem condição';
}

export function medir(linhas: readonly Registro[], atributo: string): Amostra[] {
  const medidas: Amostra[] = [];
  for (const linha of linhas) {
    const valor = linha.valores[atributo];
    if (valor != null && Number.isFinite(valor)) medidas.push({ t: linha.t, valor });
  }
  return medidas;
}

export function amplitude(linhas: readonly Amostra[]): [number, number] {
  if (!linhas.length) return [0, 1];
  let min = Infinity;
  let max = -Infinity;
  for (const { valor } of linhas) {
    min = Math.min(min, valor);
    max = Math.max(max, valor);
  }
  const passo = (max - min) / 2 || Math.max(0.1, Math.abs(max) * 0.02);
  const base = Math.floor(min / passo) * passo;
  return [base, Math.max(base + 2 * passo, Math.ceil(max / passo) * passo)];
}

export function extensoes(contagem: number, capacidade: number): number[] {
  if (!contagem) return [];
  const linhas = Math.ceil(contagem / capacidade);
  const base = Math.floor(contagem / linhas);
  const extras = contagem % linhas;
  return Array.from({ length: linhas }, (_, i) => {
    const colunas = base + (i < extras ? 1 : 0);
    return Array<number>(colunas).fill(6 / colunas);
  }).flat();
}

export function encontrarAlertas(linhas: readonly Registro[], catalogo: Catalogo, agora: number, tolerancia: number): Alerta[] {
  const lista: Alerta[] = [];
  let aberto: Alerta | undefined;
  for (const linha of linhas) {
    const gravidade = condicao(linha.status, catalogo);
    const perto = aberto && linha.t - aberto.fim.t <= tolerancia;
    if (aberto && (!perto || gravidade !== aberto.condicao)) {
      if (perto && gravidade === 'Normal') aberto.resultado = 'Normalizou';
      else if (perto && gravidade !== 'Sem condição') aberto.resultado = 'Mudou de condição';
      aberto = undefined;
    }
    if (gravidade !== 'Crítica' && gravidade !== 'Atenção') continue;
    if (!aberto) {
      aberto = { inicio: linha, fim: linha, condicao: gravidade, quantidade: 0, resultado: 'Fim sem confirmação' };
      lista.push(aberto);
    }
    aberto.fim = linha;
    aberto.quantidade++;
  }
  if (aberto && agora - aberto.fim.t <= tolerancia) aberto.resultado = 'Em curso';
  return lista;
}

export function encontrarLacunas(linhas: readonly Registro[], agora: number, tolerancia: number): Lacuna[] {
  const lista: Lacuna[] = [];
  for (let i = 1; i < linhas.length; i++) {
    if (linhas[i].t - linhas[i - 1].t > tolerancia) lista.push({ anterior: linhas[i - 1].t, retorno: linhas[i].t });
  }
  const ultima = linhas.at(-1);
  if (ultima && agora - ultima.t > tolerancia) lista.push({ anterior: ultima.t, retorno: null });
  return lista;
}
