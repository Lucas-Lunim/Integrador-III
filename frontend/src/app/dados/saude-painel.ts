import { Injectable, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { EMPTY, catchError, exhaustMap, timer } from 'rxjs';
import { Api, Saude } from './api';

@Injectable({ providedIn: 'root' })
export class SaudePainel {
  private readonly api = inject(Api);
  readonly dados = signal<Saude | null>(null);
  readonly falhou = signal(false);
  readonly intervalo = computed(() => this.dados()?.coletaIntervaloSegundos ?? 5);

  constructor() {
    timer(0, 10_000).pipe(
      exhaustMap(() => this.api.saude().pipe(catchError(() => {
        this.falhou.set(true);
        return EMPTY;
      }))),
      takeUntilDestroyed(),
    ).subscribe((saude) => {
      this.dados.set(saude);
      this.falhou.set(false);
    });
  }
}
