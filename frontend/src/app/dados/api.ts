import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';

// Formatos devolvidos pelo backend (Pi3.Backend/Endpoints/Mensagens.cs).
// Séries chegam em colunas: um array por campo, alinhados por posição.

export type Categoria = 'OPERACAO' | 'MANUTENCAO' | 'PARADA' | 'DESCONHECIDO';

export interface MaquinaAgora {
  codigo: string;
  tipo: string;
  tipoNome: string;
  setor: string;
  status: string | null;
  statusNome: string | null;
  categoria: Categoria | null;
  alerta: boolean;
  statusDesde: string | null;
  ultimaLeitura: string | null;
  valores: Record<string, number | null>;
}

export interface Agora {
  geradoEm: string;
  maquinas: MaquinaAgora[];
}

export interface Leitura {
  maquina: string;
  instante: string;
  status: string;
  valores: Record<string, number>;
}

export interface NovasLeituras {
  leituras: Leitura[];
  agora: Agora;
}

export interface Serie {
  maquina: string;
  atributos: string[];
  instantes: number[];
  status: string[];
  series: Record<string, (number | null)[]>;
}

export interface Dimensoes {
  maquinas: { codigo: string; tipo: string; setor: string }[];
  tipos: { codigo: string; nome: string; atributos: string[] }[];
  atributos: { codigo: string; nome: string; unidade: string | null; natureza: 'MEDIDA' | 'CONTADOR' }[];
  status: { codigo: string; nome: string; categoria: Categoria; alerta: boolean }[];
}

/** Uma linha por (período, grupo); cada indicador é uma coluna alinhada às linhas. */
export interface Indicadores {
  granularidade: string;
  periodos: string[];
  fimPeriodos: string[];
  grupos: { codigo: string; nome: string }[];
  periodo: number[];
  grupo: number[];
  colunas: Record<string, (number | null)[]>;
}

export interface Saude {
  agora: string;
  apiUrl: string;
  pronto: boolean;
  coleta: {
    ultimaTentativa: string | null;
    ok: boolean;
    erro: string | null;
    recebidas: number;
    novas: number;
    repetidas: number;
    invalidas: number;
    totalNovas: number;
  };
  banco: { ok: boolean; erro: string | null; lotesPendentes: number; lotesDescartados: number };
  sintese: { ultimoCiclo: string | null; ok: boolean; erro: string | null; resumo: string | null };
}

@Injectable({ providedIn: 'root' })
export class Api {
  private readonly http = inject(HttpClient);

  saude() {
    return this.http.get<Saude>('/api/saude');
  }

  dimensoes() {
    return this.http.get<Dimensoes>('/api/dimensoes');
  }

  agora() {
    return this.http.get<Agora>('/api/operacional/agora');
  }

  serie(maquina: string, minutos: number) {
    return this.http.get<Serie>(`/api/operacional/maquinas/${encodeURIComponent(maquina)}/serie`, {
      params: { minutos },
    });
  }

  tatico(granularidade: 'semana' | 'mes', periodos: number, agrupar: 'maquina' | 'planta', maquina?: string) {
    const params: Record<string, string | number> = { granularidade, periodos, agrupar };
    if (maquina) params['maquina'] = maquina;
    return this.http.get<Indicadores>('/api/tatico/indicadores', { params });
  }

  estrategico(granularidade: 'mes' | 'ano', periodos: number, agrupar: 'planta' | 'tipo') {
    return this.http.get<Indicadores>('/api/estrategico/indicadores', {
      params: { granularidade, periodos, agrupar },
    });
  }
}
