import { NgTemplateOutlet } from '@angular/common';
import { Component, Injectable, computed, effect, inject, signal, untracked } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { EMPTY, catchError, exhaustMap, from, map, mergeMap, of, timer } from 'rxjs';
import { Paginacao, Paginador, TamanhoPaginaControle } from '../../componentes/paginacao';
import { Api } from '../../dados/api';
import { Catalogo } from '../../dados/catalogo';
import { numero, horas } from '../../dados/formatos';
import { SaudePainel } from '../../dados/saude-painel';
import { TempoReal } from '../../dados/tempo-real';
import { Curva } from './curva';
import {
  Amostra, Intervalo, Periodo, Registro, amplitude, condicao, dataHora, desdeIndice, encontrarAlertas,
  encontrarLacunas, extensoes, horario, juntar, medir, minuto, periodos, registros,
} from './modelo';

@Injectable({ providedIn: 'root' })
class PreferenciasMonitor {
  readonly periodo = signal<Periodo>(periodos[1]);
}

@Component({
  selector: 'app-monitor',
  imports: [RouterLink, NgTemplateOutlet, Curva, Paginador, TamanhoPaginaControle],
  templateUrl: './monitor.html',
})
export default class Monitor {
  private readonly api = inject(Api);
  private readonly rota = inject(ActivatedRoute);
  private readonly tempoReal = inject(TempoReal);
  private readonly saude = inject(SaudePainel);
  private readonly preferencias = inject(PreferenciasMonitor);
  protected readonly catalogo = inject(Catalogo);
  protected readonly tela = this.rota.snapshot.data['tela'] as 'geral' | 'maquina' | 'alertas';
  protected readonly codigo = toSignal(this.rota.paramMap.pipe(map((p) => p.get('maquina') ?? '')), { requireSync: true });
  private readonly consulta = toSignal(this.rota.queryParamMap, { requireSync: true });

  protected readonly periodo = this.preferencias.periodo.asReadonly();
  protected readonly periodos = periodos;
  protected readonly intervalo = this.saude.intervalo;
  protected readonly tolerancia = computed(() => Math.max(15, this.intervalo() * 3) * 1000);
  protected readonly agora = toSignal(timer(0, 5000).pipe(map(() => Date.now())), { initialValue: Date.now() });
  protected readonly numero = numero;
  protected readonly dataHora = dataHora;
  protected readonly horario = horario;
  protected readonly horas = horas;

  protected readonly detalhe = signal('Operação');
  protected readonly abas = ['Operação', 'Histórico', 'Indicadores'];
  protected readonly atributo = signal('temperatura');
  protected readonly filtroMaquina = signal('');
  protected readonly gravidade = signal('');
  protected readonly ordem = signal('gravidade');
  protected readonly lista = signal<'desvios' | 'lacunas'>('desvios');

  private readonly dados = signal(new Map<string, readonly Registro[]>());
  private readonly ativos = signal<readonly string[]>([]);
  private readonly falhas = signal(new Set<string>());
  private readonly refresh = signal(0);

  protected readonly janelaDaOcorrencia = computed<Intervalo | null>(() => {
    const consulta = this.consulta();
    const inicio = Number(consulta.get('de'));
    const fim = Number(consulta.get('ate'));
    return inicio > 0 && fim >= inicio && fim <= Date.now() + minuto ? [inicio, fim] : null;
  });
  protected readonly consultaOrigem = computed(() => this.consulta().get('origem'));
  protected readonly maquinas = computed(() => this.tempoReal.agora()?.maquinas ?? []);
  protected readonly catalogado = computed(() =>
    this.maquinas().some((m) => m.codigo === this.codigo()) ||
    this.catalogo.dimensoes()?.maquinas.some((m) => m.codigo === this.codigo()),
  );
  private readonly nomesCarregados = computed(() => {
    if (this.tela === 'maquina') return this.catalogado() ? this.codigo() : '';
    return this.maquinas().map((m) => m.codigo).join(',');
  });
  protected readonly carregando = computed(() => this.ativos().some((codigo) => !this.dados().has(codigo) && !this.falhas().has(codigo)));
  protected readonly erro = computed(() => this.falhas().size > 0 || (this.saude.falhou() && !this.tempoReal.agora()));
  protected readonly pronto = computed(() => this.tempoReal.agora() !== null);
  protected readonly retencao = computed(() => this.saude.dados()?.retencaoBrutoHoras);

  private readonly recorte = computed(() => {
    const dados = this.dados();
    const janela = this.janelaDaOcorrencia();
    const ms = this.periodo().ms;
    const inicio = janela ? janela[0] - 30_000 : Number.isFinite(ms) ? this.agora() - ms : -Infinity;
    const saida = new Map<string, readonly Registro[]>();
    for (const [codigo, linhas] of dados) {
      const indice = desdeIndice(linhas, inicio);
      const fim = janela ? desdeIndice(linhas, janela[1] + 30_001) : linhas.length;
      saida.set(codigo, indice === 0 && fim === linhas.length ? linhas : linhas.slice(indice, fim));
    }
    return saida;
  });

  protected readonly ultimaDoRecorte = computed(() => {
    let ultima = 0;
    for (const linhas of this.recorte().values()) ultima = Math.max(ultima, linhas.at(-1)?.t ?? 0);
    return ultima;
  });
  protected readonly cartoes = computed(() => {
    const dados = this.recorte();
    const cartoes = this.maquinas().map((m) => {
      const linhas = dados.get(m.codigo) ?? [];
      const ultima = linhas.at(-1);
      return {
        ...m, nome: this.catalogo.nomeMaquina(m.codigo), ultima, temperaturas: medir(linhas, 'temperatura'),
        situacao: ultima ? condicao(ultima.status, this.catalogo) : 'Sem leituras',
        pendente: !dados.has(m.codigo) && !this.falhas().has(m.codigo),
        falhou: this.falhas().has(m.codigo),
        resumo: Object.entries(ultima?.valores ?? {})
          .filter(([codigo, valor]) => codigo !== 'temperatura' && valor != null && !this.catalogo.ehContador(codigo))
          .map(([codigo, valor]) => ({ codigo, valor })),
      };
    });
    const preencher = (lista: typeof cartoes) => {
      const desktop = extensoes(lista.length, 3);
      const tablet = extensoes(lista.length, 2);
      return lista.map((m, i) => ({ ...m, desktop: desktop[i], tablet: tablet[i] }));
    };
    const comLeituras = cartoes.filter((m) => m.ultima);
    return {
      comLeituras: preencher(comLeituras), semLeituras: preencher(cartoes.filter((m) => !m.ultima)),
      normal: comLeituras.filter((m) => m.situacao === 'Normal').length,
      atencao: comLeituras.filter((m) => m.situacao === 'Atenção').length,
      critica: comLeituras.filter((m) => m.situacao === 'Crítica').length,
      escala: amplitude(comLeituras.flatMap((m) => m.temperaturas)),
    };
  });
  protected readonly cartoesGeralEscala = computed(() => this.cartoes().escala);

  protected readonly linhas = computed(() => this.recorte().get(this.codigo()) ?? []);
  protected readonly ultimaDaMaquina = computed(() => this.linhas().at(-1));
  protected readonly atributos = computed(() => {
    const presentes = new Set<string>();
    for (const linha of this.linhas()) {
      for (const [codigo, valor] of Object.entries(linha.valores)) if (valor != null) presentes.add(codigo);
    }
    const medidas = [...presentes].filter((codigo) => codigo !== 'temperatura');
    if (presentes.has('temperatura')) medidas.unshift('temperatura');
    return medidas;
  });
  protected readonly atributoAtual = computed(() => this.atributos().includes(this.atributo()) ? this.atributo() : this.atributos()[0]);
  protected readonly amostras = computed(() => medir(this.linhas(), this.atributoAtual() ?? ''));
  protected readonly escala = computed(() => amplitude(this.amostras()));
  protected readonly historico = computed(() => [...this.linhas()].reverse());
  protected readonly paginaHistorico = new Paginacao(() => this.linhas().length);
  protected readonly paginaIndicadores = new Paginacao(() => this.atributos().length);

  protected readonly eventos = computed(() => {
    const dados = this.recorte();
    const agora = this.agora();
    const tolerancia = this.tolerancia();
    const maquinas = this.maquinas().map(({ codigo }) => {
      const linhas = dados.get(codigo) ?? [];
      const nome = this.catalogo.nomeMaquina(codigo);
      return {
        codigo, nome,
        alertas: encontrarAlertas(linhas, this.catalogo, agora, tolerancia),
        lacunas: encontrarLacunas(linhas, agora, tolerancia),
      };
    });
    const desvios = maquinas.flatMap(({ codigo, nome, alertas }) => alertas.map((alerta) => ({ ...alerta, codigo, nome })));
    return {
      desvios,
      criticos: desvios.filter((alerta) => alerta.condicao === 'Crítica').length,
      leituras: desvios.reduce((total, alerta) => total + alerta.quantidade, 0),
      porMaquina: maquinas.filter(({ alertas }) => alertas.length).sort((a, b) => b.alertas.length - a.alertas.length),
      lacunas: maquinas.flatMap(({ codigo, nome, lacunas }) => lacunas.map((lacuna) => ({ ...lacuna, codigo, nome }))),
    };
  });
  protected readonly desvios = computed(() => {
    const maquina = this.filtroMaquina();
    const gravidade = this.gravidade();
    const gravidadePrimeiro = this.ordem() === 'gravidade';
    return this.eventos().desvios.filter((item) => (!maquina || item.codigo === maquina) && (!gravidade || item.condicao === gravidade))
      .sort((a, b) => (gravidadePrimeiro ? Number(b.condicao === 'Crítica') - Number(a.condicao === 'Crítica') : 0) || b.fim.t - a.fim.t);
  });
  protected readonly lacunas = computed(() => this.eventos().lacunas
    .filter(({ codigo }) => !this.filtroMaquina() || this.filtroMaquina() === codigo)
    .sort((a, b) => b.anterior - a.anterior));
  protected readonly paginaAlertas = new Paginacao(() => this.lista() === 'desvios' ? this.desvios().length : this.lacunas().length);

  constructor() {
    this.tempoReal.iniciar();

    effect(() => {
      const consulta = this.consulta();
      this.codigo();
      this.detalhe.set('Operação');
      this.atributo.set('temperatura');
      this.filtroMaquina.set(consulta.get('maquina') ?? '');
      this.gravidade.set(consulta.get('condicao') ?? '');
      this.lista.set(consulta.get('lista') === 'lacunas' ? 'lacunas' : 'desvios');
      this.reiniciarPaginas();
    });

    effect((limpar) => {
      const codigos = this.nomesCarregados().split(',').filter(Boolean);
      const periodo = this.periodo();
      const janela = this.janelaDaOcorrencia();
      this.refresh();
      const snapshot = untracked(() => this.dados());
      this.ativos.set(codigos);
      this.dados.set(new Map());
      this.falhas.set(new Set());
      const pedido = this.busca(codigos, periodo, janela)
        .subscribe(({ codigo, linhas }) => {
          if (linhas === null) {
            this.falhas.update((falhas) => new Set([...falhas, codigo]));
            return;
          }
          const anterior = this.dados().get(codigo) ?? [];
          const inicio = janela ? janela[0] - 30_000 : Date.now() - periodo.ms;
          const antigas = snapshot.get(codigo) ?? [];
          const preservadas = janela
            ? antigas.slice(desdeIndice(antigas, inicio), desdeIndice(antigas, janela[1] + 30_001))
            : antigas.slice(desdeIndice(antigas, inicio));
          this.gravar(codigo, juntar(juntar(linhas, preservadas), anterior));
        });
      limpar(() => pedido.unsubscribe());
    });

    this.tempoReal.leituras.pipe(takeUntilDestroyed()).subscribe((leituras) => {
      if (this.janelaDaOcorrencia()) return;
      const porMaquina = new Map<string, Registro[]>();
      for (const l of leituras) {
        if (!this.ativos().includes(l.maquina)) continue;
        const linhas = porMaquina.get(l.maquina) ?? [];
        linhas.push({ t: Date.parse(l.instante), status: l.status, valores: l.valores });
        porMaquina.set(l.maquina, linhas);
      }
      for (const [codigo, linhas] of porMaquina) {
        this.gravar(codigo, juntar(this.dados().get(codigo) ?? [], linhas.sort((a, b) => a.t - b.t)));
      }
    });

    effect((limpar) => {
      if (this.janelaDaOcorrencia()) return;
      const recarga = timer(5000, 5000).pipe(
        exhaustMap(() => this.tempoReal.conectado() ? EMPTY : this.busca(this.ativos(), periodos[1], null)),
      ).subscribe(({ codigo, linhas }) => {
        if (!linhas?.length) return;
        const anteriores = this.dados().get(codigo) ?? [];
        if ((linhas.at(-1)?.t ?? 0) > (anteriores.at(-1)?.t ?? 0)) this.gravar(codigo, juntar(anteriores, linhas));
      });
      limpar(() => recarga.unsubscribe());
    });
  }

  protected nome(codigo: string) {
    const nome = this.catalogo.nomeMaquina(codigo);
    return nome !== codigo ? nome : this.maquinas().find((m) => m.codigo === codigo)?.tipoNome ?? codigo;
  }

  protected unidade(codigo: string) {
    return this.catalogo.unidade(codigo) ?? (codigo === 'temperatura' ? '°C' : null);
  }

  protected rotulo(codigo: string) {
    const unidade = this.unidade(codigo);
    return this.catalogo.nomeAtributo(codigo) + (unidade ? ` (${unidade})` : '');
  }

  protected situacao(linha: Registro) {
    return condicao(linha.status, this.catalogo);
  }

  protected resumir(codigo: string) {
    const amostras: Amostra[] = medir(this.linhas(), codigo);
    let min = Infinity;
    let max = -Infinity;
    let soma = 0;
    for (const { valor } of amostras) {
      min = Math.min(min, valor);
      max = Math.max(max, valor);
      soma += valor;
    }
    return [amostras.at(-1)?.valor, min, soma / amostras.length, max];
  }

  protected trocarPeriodo(valor: string) {
    const periodo = periodos.find((p) => p.id === valor);
    if (!periodo) return;
    this.preferencias.periodo.set(periodo);
    this.reiniciarPaginas();
  }

  protected trocarDetalhe(aba: string) {
    this.detalhe.set(aba);
    this.reiniciarPaginas();
  }

  protected trocarLista(lista: 'desvios' | 'lacunas') {
    this.lista.set(lista);
    this.paginaAlertas.reiniciar();
  }

  protected recarregar() {
    this.catalogo.carregar();
    this.refresh.update((n) => n + 1);
  }

  private reiniciarPaginas() {
    this.paginaHistorico.reiniciar();
    this.paginaIndicadores.reiniciar();
    this.paginaAlertas.reiniciar();
  }

  private gravar(codigo: string, linhas: readonly Registro[]) {
    const ms = this.periodo().ms;
    const inicio = this.janelaDaOcorrencia() ? -Infinity : Date.now() - ms - minuto;
    const indice = desdeIndice(linhas, inicio);
    this.dados.update((dados) => new Map(dados).set(codigo, indice ? linhas.slice(indice) : linhas));
  }

  private busca(codigos: readonly string[], periodo: Periodo, janela: Intervalo | null) {
    return from(codigos).pipe(mergeMap((codigo) => {
      const desde = janela ? janela[0] - 30_000 : Number.isFinite(periodo.ms) ? Date.now() - periodo.ms - minuto : undefined;
      const requisicao = janela || periodo.ms > 5 * minuto
        ? this.api.historico(codigo, desde, janela ? janela[1] + 30_000 : undefined)
        : this.api.serie(codigo, Math.ceil(periodo.ms / minuto) + 1);
      return requisicao.pipe(
        map((serie) => ({ codigo, linhas: registros(serie) })),
        catchError(() => of({ codigo, linhas: null })),
      );
    }, 4));
  }
}
