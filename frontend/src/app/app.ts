import { DOCUMENT } from '@angular/common';
import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';
import { Relogio } from './dados/relogio';
import { haQuanto } from './dados/formatos';
import { SaudePainel } from './dados/saude-painel';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app.html',
})
export class App {
  private readonly documento = inject(DOCUMENT);
  private readonly router = inject(Router);
  private readonly saudePainel = inject(SaudePainel);
  private readonly relogio = inject(Relogio);
  protected readonly titulo = toSignal(
    this.router.events.pipe(
      filter((evento) => evento instanceof NavigationEnd),
      map(() => this.router.routerState.snapshot.root.firstChild?.data['secao'] ?? 'Visão geral'),
    ),
    { initialValue: 'Visão geral' },
  );

  protected readonly situacao = computed(() => {
    const s = this.saudePainel.dados();
    if (this.saudePainel.falhou()) return { classe: 'erro', texto: 'Backend fora do ar', detalhe: '' };
    if (!s) return { classe: '', texto: 'Conectando…', detalhe: '' };
    if (!s.pronto) return { classe: 'erro', texto: 'Aguardando banco de dados', detalhe: '' };
    if (!s.coleta.ok) return { classe: 'erro', texto: 'Coleta com falha', detalhe: s.coleta.erro ?? '' };
    if (!s.banco.ok) return { classe: '', texto: 'Banco indisponível', detalhe: s.banco.erro ?? '' };
    return { classe: 'ok', texto: `Coleta ok · ${haQuanto(s.coleta.ultimaTentativa, this.relogio.agora())}`, detalhe: '' };
  });

  protected irAoConteudo(evento: Event) {
    evento.preventDefault();
    this.documento.getElementById('conteudo')?.focus();
  }
}
