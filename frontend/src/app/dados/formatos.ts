// Formatação para a tela (pt-BR, horário de Brasília). Nenhuma regra de negócio aqui.

const FUSO = 'America/Sao_Paulo';
const formatadores = new Map<number, Intl.NumberFormat>();

function formatador(casas: number) {
  let f = formatadores.get(casas);
  if (!f) {
    f = new Intl.NumberFormat('pt-BR', { maximumFractionDigits: casas });
    formatadores.set(casas, f);
  }
  return f;
}

const significativos = new Intl.NumberFormat('pt-BR', { maximumSignificantDigits: 3 });

/** Número com casas decimais proporcionais ao tamanho (1.234 · 56,7 · 1,54 · 0,000146). */
export function numero(v: number | null | undefined, casas = 2): string {
  if (v == null || !Number.isFinite(v)) return '—';
  const abs = Math.abs(v);
  // Valores muito pequenos (horas_operacao de algumas máquinas) sumiriam no arredondamento.
  if (abs > 0 && abs < 0.01) return significativos.format(v);
  return formatador(abs >= 1000 ? 0 : abs >= 100 ? Math.min(casas, 1) : casas).format(v);
}

export function inteiro(v: number | null | undefined): string {
  return v == null || !Number.isFinite(v) ? '—' : formatador(0).format(Math.round(v));
}

export function percentual(v: number | null | undefined, casas = 1): string {
  if (v == null || !Number.isFinite(v)) return '—';
  return new Intl.NumberFormat('pt-BR', { style: 'percent', maximumFractionDigits: casas }).format(v);
}

export function horas(h: number | null | undefined): string {
  if (h == null || !Number.isFinite(h)) return '—';
  if (h === 0) return '0 h';
  if (h < 1 / 60) return `${Math.round(h * 3600)} s`;
  if (h < 1) return `${Math.round(h * 60)} min`;
  const inteiras = Math.floor(h);
  const minutos = Math.round((h - inteiras) * 60);
  return minutos ? `${inteiras} h ${minutos} min` : `${inteiras} h`;
}

/** "há 5 s", "há 3 min", "há 2 h", "há 4 dias". */
export function haQuanto(iso: string | null | undefined, agoraMs: number): string {
  if (!iso) return 'sem leitura';
  const s = Math.max(0, Math.round((agoraMs - Date.parse(iso)) / 1000));
  if (s < 60) return `há ${s} s`;
  if (s < 3600) return `há ${Math.floor(s / 60)} min`;
  if (s < 86400) return `há ${Math.floor(s / 3600)} h`;
  return `há ${Math.floor(s / 86400)} dias`;
}

/** Duração desde um instante: "12 min", "3 h 5 min". */
export function desde(iso: string | null | undefined, agoraMs: number): string {
  if (!iso) return '—';
  return horas(Math.max(0, agoraMs - Date.parse(iso)) / 3_600_000);
}

export function hora(ms: number): string {
  return new Date(ms).toLocaleTimeString('pt-BR', { timeZone: FUSO, hour: '2-digit', minute: '2-digit' });
}

export function horaCompleta(iso: string | number): string {
  return new Date(iso).toLocaleTimeString('pt-BR', { timeZone: FUSO });
}

export function dataHora(iso: string | null | undefined): string {
  if (!iso) return '—';
  return new Date(iso).toLocaleString('pt-BR', { timeZone: FUSO, dateStyle: 'short', timeStyle: 'short' });
}

/** Rótulo curto de um período: "sem. 09/11", "nov/26", "2026". */
export function rotuloPeriodo(iso: string, granularidade: string): string {
  const d = new Date(iso);
  const parte = (o: Intl.DateTimeFormatOptions) => d.toLocaleDateString('pt-BR', { timeZone: FUSO, ...o });
  switch (granularidade) {
    case 'semana':
      return `sem. ${parte({ day: '2-digit', month: '2-digit' })}`;
    case 'mes':
      return parte({ month: 'short', year: '2-digit' }).replace('. de ', '/').replace(' de ', '/');
    case 'ano':
      return parte({ year: 'numeric' });
    default:
      return parte({ day: '2-digit', month: '2-digit' });
  }
}

/** Rótulo longo: "semana de 09/11 a 15/11/2026", "novembro de 2026". */
export function descricaoPeriodo(inicio: string, fim: string, granularidade: string): string {
  const opcoes = { timeZone: FUSO } as const;
  const ini = new Date(inicio);
  const ultimoDia = new Date(Date.parse(fim) - 1);
  switch (granularidade) {
    case 'semana':
      return `semana de ${ini.toLocaleDateString('pt-BR', { ...opcoes, day: '2-digit', month: '2-digit' })} a ${ultimoDia.toLocaleDateString('pt-BR', opcoes)}`;
    case 'mes':
      return ini.toLocaleDateString('pt-BR', { ...opcoes, month: 'long', year: 'numeric' });
    case 'ano':
      return ini.toLocaleDateString('pt-BR', { ...opcoes, year: 'numeric' });
    default:
      return ini.toLocaleDateString('pt-BR', opcoes);
  }
}
