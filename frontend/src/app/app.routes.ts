import { Routes } from '@angular/router';

// Uma área por nível da pirâmide. Cada uma é carregada separadamente
// (lazy loading), então o operador não baixa o código do estratégico.
export const routes: Routes = [
  { path: '', title: 'Painel Industrial · PI III', loadComponent: () => import('./niveis/inicio') },
  {
    path: 'operacional',
    title: 'Operacional · PI III',
    loadComponent: () => import('./niveis/operacional/operacional'),
  },
  {
    path: 'operacional/:maquina',
    title: 'Máquina · PI III',
    loadComponent: () => import('./niveis/operacional/maquina'),
  },
  { path: 'tatico', title: 'Tático · PI III', loadComponent: () => import('./niveis/tatico/tatico') },
  {
    path: 'estrategico',
    title: 'Estratégico · PI III',
    loadComponent: () => import('./niveis/estrategico/estrategico'),
  },
  { path: '**', redirectTo: '' },
];
