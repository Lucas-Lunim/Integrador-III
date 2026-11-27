import { Routes } from '@angular/router';
import { exigeLogin, exigeNivel, somenteDeslogado } from './acesso/sessao';

// Uma área por nível da pirâmide. Cada uma é carregada separadamente
// (lazy loading), então o operador não baixa o código do estratégico.
// Acesso (protótipo, ver docs/usuarios.md): operador vê o operacional; tático,
// operacional e tático; estratégico, todos.
export const routes: Routes = [
  {
    path: 'login',
    title: 'Entrar · PI III',
    canMatch: [somenteDeslogado],
    loadComponent: () => import('./acesso/login'),
  },
  {
    path: '',
    title: 'Painel Industrial · PI III',
    canMatch: [exigeLogin],
    loadComponent: () => import('./niveis/inicio'),
  },
  {
    path: 'operacional',
    title: 'Operacional · PI III',
    canMatch: [exigeNivel('operacional')],
    loadComponent: () => import('./niveis/operacional/operacional'),
  },
  {
    path: 'operacional/:maquina',
    title: 'Máquina · PI III',
    canMatch: [exigeNivel('operacional')],
    loadComponent: () => import('./niveis/operacional/maquina'),
  },
  {
    path: 'tatico',
    title: 'Tático · PI III',
    canMatch: [exigeNivel('tatico')],
    loadComponent: () => import('./niveis/tatico/tatico'),
  },
  {
    path: 'estrategico',
    title: 'Estratégico · PI III',
    canMatch: [exigeNivel('estrategico')],
    loadComponent: () => import('./niveis/estrategico/estrategico'),
  },
  { path: '**', redirectTo: '' },
];
