import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Catalogo } from '../dados/catalogo';

@Component({
  selector: 'app-inicio',
  imports: [RouterLink],
  template: `
    <main class="pagina">
      <div class="cabecalho-pagina">
        <div>
          <span class="nivel-tag">Projeto Integrador III</span>
          <h1>Sistema integrado de dados industriais</h1>
          <p>
            Leituras de {{ maquinas() }} máquinas do Setor1, coletadas da API a cada 5 segundos. Escolha o nível:
            quanto mais alto na pirâmide, mais resumida é a informação.
          </p>
        </div>
      </div>

      <div class="niveis">
        <a class="nivel" routerLink="/operacional">
          <span class="degrau">1</span>
          <h2>Operacional</h2>
          <p>Para quem acompanha a máquina. Status e valores ao vivo, histórico da última hora.</p>
          <span class="fonte">Fonte: memória do coletor · atualização a cada leitura</span>
        </a>
        <a class="nivel" routerLink="/tatico">
          <span class="degrau">2</span>
          <h2>Tático</h2>
          <p>Para supervisão e gerência. Produção, perdas, disponibilidade e desgaste por semana e por mês.</p>
          <span class="fonte">Fonte: resumos semanais e mensais</span>
        </a>
        <a class="nivel" routerLink="/estrategico">
          <span class="degrau">3</span>
          <h2>Estratégico</h2>
          <p>Para a diretoria da planta. Tendências de meses e anos e comparação entre tipos de máquina.</p>
          <span class="fonte">Fonte: resumos mensais e anuais</span>
        </a>
      </div>
    </main>
  `,
  styles: `
    .niveis { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 14px; }
    .nivel {
      display: flex; flex-direction: column; gap: 8px; padding: 20px; border-radius: 12px; text-decoration: none;
      color: var(--tinta); background: var(--superficie); border: 1px solid var(--borda); box-shadow: var(--sombra);
    }
    .nivel:hover { border-color: var(--acao); }
    .nivel h2 { font-size: 1.15rem; }
    .nivel p { margin: 0; color: var(--tinta-2); flex: 1; }
    .degrau {
      display: grid; place-items: center; width: 32px; height: 32px; border-radius: 8px;
      background: var(--acao-suave); color: var(--acao); font-weight: 800;
    }
    .fonte { font-size: 0.78rem; color: var(--apagado); }
    @media (max-width: 800px) { .niveis { grid-template-columns: 1fr; } }
  `,
})
export default class Inicio {
  private readonly catalogo = inject(Catalogo);
  protected readonly maquinas = computed(() => this.catalogo.dimensoes()?.maquinas.length ?? 0);
}
