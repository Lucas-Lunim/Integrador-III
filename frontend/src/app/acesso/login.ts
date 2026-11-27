import { Component, inject, input, signal } from '@angular/core';
import { Router } from '@angular/router';
import {
  CONTAS_DEMONSTRACAO,
  NIVEIS_DO_PERFIL,
  NOME_DO_NIVEL,
  NOME_DO_PERFIL,
  Perfil,
  Sessao,
} from './sessao';

@Component({
  selector: 'app-login',
  template: `
    <main class="pagina login">
      <section class="cartao entrada" aria-labelledby="titulo-login">
        <span class="nivel-tag">Painel Industrial · PI III</span>
        <h1 id="titulo-login">Entrar</h1>
        <p class="descricao">O seu perfil define quais níveis da pirâmide você vê.</p>

        <form (submit)="entrar($event)" novalidate>
          <div class="campo">
            <label for="usuario">Usuário</label>
            <input
              id="usuario"
              name="usuario"
              autocomplete="username"
              autocapitalize="none"
              spellcheck="false"
              required
              autofocus
              [value]="login()"
              (input)="login.set(valor($event)); erro.set(false)"
            />
          </div>
          <div class="campo">
            <label for="senha">Senha</label>
            <input
              id="senha"
              name="senha"
              type="password"
              autocomplete="current-password"
              required
              [value]="senha()"
              (input)="senha.set(valor($event)); erro.set(false)"
            />
          </div>
          @if (erro()) {
            <p class="aviso erro" role="alert">Usuário ou senha incorretos.</p>
          }
          <button type="submit" class="botao">Entrar</button>
        </form>
      </section>

      <section class="cartao contas" aria-labelledby="titulo-contas">
        <h2 id="titulo-contas">Contas de demonstração</h2>
        <p class="descricao">Protótipo: clique no usuário para preencher o formulário.</p>
        <div class="tabela-rolagem">
          <table class="dados">
            <thead>
              <tr>
                <th>Usuário</th>
                <th>Senha</th>
                <th>Perfil</th>
                <th>Níveis que vê</th>
              </tr>
            </thead>
            <tbody>
              @for (c of contas; track c.login) {
                <tr>
                  <td>
                    <button type="button" class="conta" (click)="preencher(c.login, c.senha)">
                      {{ c.login }}
                    </button>
                  </td>
                  <td>
                    <code>{{ c.senha }}</code>
                  </td>
                  <td>{{ nomePerfil[c.perfil] }}</td>
                  <td>{{ niveis(c.perfil) }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      </section>
    </main>
  `,
  styles: `
    .login {
      display: grid;
      grid-template-columns: minmax(0, 380px) minmax(0, 560px);
      gap: 16px;
      justify-content: center;
      align-items: start;
      padding-top: 48px;
    }
    .entrada h1 {
      font-size: 1.45rem;
      letter-spacing: -0.01em;
    }
    form {
      display: grid;
      gap: 14px;
      margin-top: 4px;
    }
    input {
      width: 100%;
      padding: 8px 10px;
      border: 1px solid var(--borda);
      border-radius: 8px;
      background: var(--superficie);
    }
    input:focus-visible,
    .botao:focus-visible,
    .conta:focus-visible {
      outline: 2px solid var(--acao);
      outline-offset: 2px;
    }
    .botao {
      padding: 9px 14px;
      border: 0;
      border-radius: 8px;
      background: var(--acao);
      color: #fff;
      font-weight: 700;
      cursor: pointer;
    }
    .botao:hover {
      filter: brightness(1.08);
    }
    .aviso {
      margin: 0;
    }
    .contas h2 {
      font-size: 1rem;
    }
    .conta {
      border: 0;
      background: none;
      padding: 0;
      color: var(--acao);
      font-weight: 600;
      cursor: pointer;
      text-decoration: underline;
      text-underline-offset: 2px;
    }
    code {
      font-family: ui-monospace, 'Cascadia Code', monospace;
      font-size: 0.85rem;
    }
    @media (max-width: 900px) {
      .login {
        grid-template-columns: minmax(0, 1fr);
        padding-top: 16px;
      }
    }
  `,
})
export default class Login {
  private readonly sessao = inject(Sessao);
  private readonly router = inject(Router);

  /** Tela que a pessoa tentou abrir antes de entrar (?voltar=/tatico). */
  readonly voltar = input<string>();

  protected readonly login = signal('');
  protected readonly senha = signal('');
  protected readonly erro = signal(false);
  protected readonly contas = CONTAS_DEMONSTRACAO;
  protected readonly nomePerfil = NOME_DO_PERFIL;

  protected valor(evento: Event): string {
    return (evento.target as HTMLInputElement).value;
  }

  protected niveis(perfil: Perfil): string {
    return NIVEIS_DO_PERFIL[perfil].map((n) => NOME_DO_NIVEL[n]).join(', ');
  }

  protected preencher(login: string, senha: string) {
    this.login.set(login);
    this.senha.set(senha);
    this.erro.set(false);
  }

  protected entrar(evento: Event) {
    evento.preventDefault();
    if (!this.sessao.entrar(this.login(), this.senha())) {
      this.erro.set(true);
      this.senha.set('');
      return;
    }
    // Só caminhos internos ("/tatico"); se o perfil não puder ver, a guarda devolve ao início.
    const destino = this.voltar();
    this.router.navigateByUrl(
      destino?.startsWith('/') && !destino.startsWith('//') ? destino : '/',
    );
  }
}
