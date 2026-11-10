import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Api, Saude } from './dados/api';
import { haQuanto } from './dados/formatos';
import { Relogio } from './dados/relogio';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  private readonly api = inject(Api);
  private readonly relogio = inject(Relogio);
  protected readonly saude = signal<Saude | null>(null);
  protected readonly semResposta = signal(false);

  /** Resumo da coleta mostrado no topo de todas as telas. */
  protected readonly situacao = computed(() => {
    const s = this.saude();
    const agora = this.relogio.agora();
    if (this.semResposta()) return { classe: 'erro', texto: 'Backend fora do ar' };
    if (!s) return { classe: '', texto: 'Conectando…' };
    if (!s.pronto) return { classe: 'erro', texto: 'Aguardando o banco de dados' };
    if (!s.coleta.ok) return { classe: 'erro', texto: s.coleta.erro ?? 'Coleta com falha' };
    if (!s.banco.ok) return { classe: 'alerta', texto: `Banco indisponível · ${s.banco.lotesPendentes} lotes na fila` };
    return { classe: 'ok', texto: `Coleta ok · ${haQuanto(s.coleta.ultimaTentativa, agora)}` };
  });

  constructor() {
    const atualizar = () =>
      this.api.saude().subscribe({
        next: (s) => {
          this.saude.set(s);
          this.semResposta.set(false);
        },
        error: () => this.semResposta.set(true),
      });
    atualizar();
    setInterval(atualizar, 10_000);
  }
}
