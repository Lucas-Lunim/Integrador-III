import { Routes } from '@angular/router';

const monitor = () => import('./niveis/monitor/monitor');

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'operacional' },
  { path: 'operacional', title: 'Visão geral · Monitor Industrial', data: { tela: 'geral', secao: 'Visão geral' }, loadComponent: monitor },
  { path: 'operacional/:maquina', title: 'Equipamento · Monitor Industrial', data: { tela: 'maquina', secao: 'Equipamento' }, loadComponent: monitor },
  { path: 'alertas', title: 'Alertas do recorte · Monitor Industrial', data: { tela: 'alertas', secao: 'Alertas do recorte' }, loadComponent: monitor },
  { path: 'tatico', title: 'Tático · Monitor Industrial', data: { secao: 'Tático' }, loadComponent: () => import('./niveis/tatico/tatico') },
  { path: 'estrategico', title: 'Estratégico · Monitor Industrial', data: { secao: 'Estratégico' }, loadComponent: () => import('./niveis/estrategico/estrategico') },
  { path: '**', redirectTo: 'operacional' },
];
