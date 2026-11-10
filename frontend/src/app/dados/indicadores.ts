import { Indicadores } from './api';

/** Valor de um indicador numa linha (período, grupo). O backend ordena as linhas por período e depois por grupo. */
export function celula(ind: Indicadores | null, coluna: string, periodo: number, grupo: number): number | null {
  if (!ind) return null;
  return ind.colunas[coluna]?.[periodo * ind.grupos.length + grupo] ?? null;
}

/** Série de um indicador para um grupo, ao longo dos períodos. */
export function serieDoGrupo(ind: Indicadores, coluna: string, grupo: number): (number | null)[] {
  return ind.periodos.map((_, p) => celula(ind, coluna, p, grupo));
}

/** Variação relativa entre dois valores: +12% / -3%. Null se não houver base. */
export function variacao(atual: number | null, anterior: number | null): number | null {
  if (atual == null || anterior == null || anterior === 0) return null;
  return (atual - anterior) / Math.abs(anterior);
}
