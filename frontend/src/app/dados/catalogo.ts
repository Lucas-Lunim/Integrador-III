import { Injectable, computed, inject, signal } from '@angular/core';
import { Api, Categoria, Dimensoes } from './api';

/** Cadastros (máquinas, atributos, status) e os rótulos derivados deles. */
@Injectable({ providedIn: 'root' })
export class Catalogo {
  private readonly api = inject(Api);
  readonly dimensoes = signal<Dimensoes | null>(null);

  private readonly atributos = computed(
    () => new Map(this.dimensoes()?.atributos.map((a) => [a.codigo, a]) ?? []),
  );
  private readonly tipos = computed(() => new Map(this.dimensoes()?.tipos.map((t) => [t.codigo, t]) ?? []));
  private readonly maquinas = computed(
    () => new Map(this.dimensoes()?.maquinas.map((m, i) => [m.codigo, { ...m, indice: i }]) ?? []),
  );
  private readonly status = computed(() => new Map(this.dimensoes()?.status.map((s) => [s.codigo, s]) ?? []));

  constructor() {
    this.carregar();
    // Máquinas e atributos novos são cadastrados pelo coletor a qualquer momento.
    setInterval(() => this.carregar(), 60_000);
  }

  carregar() {
    this.api.dimensoes().subscribe({ next: (d) => this.dimensoes.set(d), error: () => {} });
  }

  nomeAtributo(codigo: string): string {
    return this.atributos().get(codigo)?.nome ?? codigo;
  }

  unidade(codigo: string): string | null {
    return this.atributos().get(codigo)?.unidade ?? null;
  }

  /** "Consumo de energia (kW)"; sem unidade confirmada, só o nome. */
  rotuloAtributo(codigo: string): string {
    const unidade = this.unidade(codigo);
    return unidade ? `${this.nomeAtributo(codigo)} (${unidade})` : this.nomeAtributo(codigo);
  }

  ehContador(codigo: string): boolean {
    return this.atributos().get(codigo)?.natureza === 'CONTADOR';
  }

  /** "TORNO_CNC_01" → "Torno CNC 01" (nome do tipo + número da máquina). */
  nomeMaquina(codigo: string): string {
    const maquina = this.maquinas().get(codigo);
    const tipo = maquina ? this.tipos().get(maquina.tipo)?.nome : undefined;
    const numero = /(\d+)$/.exec(codigo)?.[1];
    return tipo ? (numero ? `${tipo} ${numero}` : tipo) : codigo;
  }

  /**
   * Posição fixa da máquina na paleta: segue a ordem de cadastro, nunca a
   * posição na tela, para a cor não mudar quando um filtro esconde máquinas.
   * Da 7ª em diante, cinza (a paleta validada tem 6 cores distinguíveis).
   */
  indiceCor(codigo: string): number {
    const i = this.maquinas().get(codigo)?.indice ?? -1;
    return i >= 0 && i < 6 ? i : -1;
  }

  corMaquina(codigo: string): string {
    const i = this.indiceCor(codigo);
    return i >= 0 ? `var(--s${i + 1})` : 'var(--apagado)';
  }

  categoriaStatus(codigo: string): Categoria {
    return this.status().get(codigo)?.categoria ?? 'DESCONHECIDO';
  }

  alertaStatus(codigo: string): boolean {
    return this.status().get(codigo)?.alerta ?? false;
  }

  nomeStatus(codigo: string): string {
    return this.status().get(codigo)?.nome ?? codigo;
  }
}
