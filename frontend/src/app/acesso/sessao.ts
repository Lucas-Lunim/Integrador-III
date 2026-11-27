import { Injectable, computed, inject, signal } from '@angular/core';
import { CanMatchFn, Router, UrlSegment } from '@angular/router';

export type Nivel = 'operacional' | 'tatico' | 'estrategico';
export type Perfil = 'operador' | 'tatico' | 'estrategico';

export interface Usuario {
  login: string;
  nome: string;
  perfil: Perfil;
}

/** Quanto mais alto o perfil na pirâmide, mais níveis ele vê. */
export const NIVEIS_DO_PERFIL: Record<Perfil, readonly Nivel[]> = {
  operador: ['operacional'],
  tatico: ['operacional', 'tatico'],
  estrategico: ['operacional', 'tatico', 'estrategico'],
};

export const NOME_DO_PERFIL: Record<Perfil, string> = {
  operador: 'Operador',
  tatico: 'Tático',
  estrategico: 'Estratégico',
};

export const NOME_DO_NIVEL: Record<Nivel, string> = {
  operacional: 'Operacional',
  tatico: 'Tático',
  estrategico: 'Estratégico',
};

/**
 * PROTÓTIPO: contas de demonstração fixas no frontend, sem banco de dados.
 * O login só decide quais telas aparecem; as rotas /api do backend continuam
 * abertas. Lista e limitações em docs/usuarios.md.
 */
export const CONTAS_DEMONSTRACAO: readonly (Usuario & { senha: string })[] = [
  { login: 'operador', senha: 'pi3', nome: 'Operador de máquina', perfil: 'operador' },
  { login: 'tatico', senha: 'pi3', nome: 'Supervisão', perfil: 'tatico' },
  { login: 'estrategico', senha: 'pi3', nome: 'Diretoria', perfil: 'estrategico' },
];

const CHAVE = 'pi3.sessao';

/** Guarda só o login; o perfil vem sempre da lista de contas. */
function contaSalva(): Usuario | null {
  try {
    const login = sessionStorage.getItem(CHAVE);
    return publica(CONTAS_DEMONSTRACAO.find((c) => c.login === login));
  } catch {
    return null;
  }
}

function publica(conta: (Usuario & { senha: string }) | undefined): Usuario | null {
  return conta ? { login: conta.login, nome: conta.nome, perfil: conta.perfil } : null;
}

/**
 * Quem está logado. Fica no sessionStorage: atualizar a página mantém a
 * sessão, fechar a aba encerra.
 */
@Injectable({ providedIn: 'root' })
export class Sessao {
  readonly usuario = signal<Usuario | null>(contaSalva());
  readonly niveis = computed(() => {
    const u = this.usuario();
    return u ? NIVEIS_DO_PERFIL[u.perfil] : [];
  });

  pode(nivel: Nivel): boolean {
    return this.niveis().includes(nivel);
  }

  /** Devolve false se o usuário ou a senha não conferirem. */
  entrar(login: string, senha: string): boolean {
    const conta = CONTAS_DEMONSTRACAO.find(
      (c) => c.login === login.trim().toLowerCase() && c.senha === senha,
    );
    if (!conta) return false;
    try {
      sessionStorage.setItem(CHAVE, conta.login);
    } catch {
      // Sem sessionStorage (janela privada restrita): a sessão vale até recarregar.
    }
    this.usuario.set(publica(conta));
    return true;
  }

  sair() {
    try {
      sessionStorage.removeItem(CHAVE);
    } catch {
      // nada a limpar
    }
    this.usuario.set(null);
  }
}

const url = (segmentos: UrlSegment[]) => '/' + segmentos.map((s) => s.path).join('/');

/*
 * As guardas usam canMatch: além de bloquear a tela, o navegador nem baixa o
 * código de um nível sem permissão (cada nível é carregado sob demanda).
 */

/** Sem login, vai para /login lembrando a tela pedida. */
export const exigeLogin: CanMatchFn = (_rota, segmentos) =>
  inject(Sessao).usuario()
    ? true
    : inject(Router).createUrlTree(['/login'], { queryParams: { voltar: url(segmentos) } });

/** Sem login, vai para /login; sem permissão para o nível, volta ao início com aviso. */
export const exigeNivel =
  (nivel: Nivel): CanMatchFn =>
  (_rota, segmentos) => {
    const sessao = inject(Sessao);
    const router = inject(Router);
    if (!sessao.usuario()) {
      return router.createUrlTree(['/login'], { queryParams: { voltar: url(segmentos) } });
    }
    return sessao.pode(nivel)
      ? true
      : router.createUrlTree(['/'], { queryParams: { semAcesso: nivel } });
  };

/** Quem já entrou não vê a tela de login. */
export const somenteDeslogado: CanMatchFn = () =>
  inject(Sessao).usuario() ? inject(Router).createUrlTree(['/']) : true;
