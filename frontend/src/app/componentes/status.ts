import { Component, computed, input } from '@angular/core';
import { Categoria } from '../dados/api';

const ICONES: Record<string, string> = {
  operacao: '✓',
  atencao: '!',
  manutencao: '⚙',
  parada: '■',
  desconhecido: '?',
};

/** Status da máquina: cor reservada de status + ícone + texto (nunca só a cor). */
@Component({
  selector: 'app-status',
  template: `<span [class]="'status ' + classe()"
    ><span class="icone" aria-hidden="true">{{ icone() }}</span>{{ nome() ?? 'Sem leitura' }}</span
  >`,
})
export class StatusMaquina {
  readonly categoria = input<Categoria | null>(null);
  readonly alerta = input(false);
  readonly nome = input<string | null>(null);

  protected readonly classe = computed(() => {
    const c = this.categoria();
    if (!c) return 'desconhecido';
    return c === 'OPERACAO' && this.alerta() ? 'atencao' : c.toLowerCase();
  });
  protected readonly icone = computed(() => ICONES[this.classe()] ?? '?');
}
