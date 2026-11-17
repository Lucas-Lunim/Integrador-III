import { Component, Injectable, computed, inject, input, signal } from '@angular/core';

const tamanhos = [10, 25, 50, 100] as const;
type TamanhoPagina = (typeof tamanhos)[number];

@Injectable({ providedIn: 'root' })
class PreferenciaDePagina {
  readonly tamanho = signal<TamanhoPagina>(10);
}

export class Paginacao {
  private readonly preferencia = inject(PreferenciaDePagina);
  private readonly indice = signal(0);
  readonly tamanho = this.preferencia.tamanho.asReadonly();
  readonly paginas = computed(() => Math.max(1, Math.ceil(this.total() / this.tamanho())));
  readonly pagina = computed(() => Math.min(this.indice(), this.paginas() - 1));
  readonly inicio = computed(() => this.pagina() * this.tamanho());
  readonly fim = computed(() => Math.min(this.inicio() + this.tamanho(), this.total()));

  constructor(readonly total: () => number) {}

  recorte<T>(registros: readonly T[]): readonly T[] {
    return registros.slice(this.inicio(), this.fim());
  }

  reiniciar() {
    this.indice.set(0);
  }

  definirTamanho(valor: string) {
    const tamanho = tamanhos.find((opcao) => String(opcao) === valor);
    if (tamanho === undefined) return;
    this.preferencia.tamanho.set(tamanho);
    this.reiniciar();
  }

  mover(direcao: number) {
    this.indice.set(Math.max(0, Math.min(this.paginas() - 1, this.pagina() + direcao)));
  }
}

@Component({
  selector: 'app-tamanho-pagina',
  template: `
    <label class="page-size">
      <span>Linhas por página</span>
      <select #opcoes (change)="paginacao().definirTamanho(opcoes.value)">
        @for (tamanho of tamanhos; track tamanho) {
          <option [value]="tamanho" [selected]="tamanho === paginacao().tamanho()">{{ tamanho }}</option>
        }
      </select>
    </label>
  `,
})
export class TamanhoPaginaControle {
  readonly paginacao = input.required<Paginacao>();
  protected readonly tamanhos = tamanhos;
}

@Component({
  selector: 'app-paginador',
  template: `
    @let lista = paginacao();
    @if (lista.total()) {
      <div class="pagination" role="group" [attr.aria-label]="rotulo()">
        @if (lista.paginas() > 1) {
          <button type="button" (click)="lista.mover(-1)" [disabled]="lista.pagina() === 0">Anterior</button>
        }
        <span class="page-status" aria-live="polite">
          {{ lista.inicio() + 1 }}–{{ lista.fim() }} de {{ lista.total() }}
          @if (lista.paginas() > 1) { · Página {{ lista.pagina() + 1 }} de {{ lista.paginas() }} }
        </span>
        @if (lista.paginas() > 1) {
          <button type="button" (click)="lista.mover(1)" [disabled]="lista.pagina() + 1 >= lista.paginas()">Próxima</button>
        }
      </div>
    }
  `,
})
export class Paginador {
  readonly paginacao = input.required<Paginacao>();
  readonly rotulo = input('Paginação');
}
