import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MaquinaAgora } from '../../dados/api';
import { Catalogo } from '../../dados/catalogo';
import { desde, haQuanto, numero } from '../../dados/formatos';
import { Relogio } from '../../dados/relogio';
import { TempoReal } from '../../dados/tempo-real';
import { StatusMaquina } from '../../componentes/status';

/** Leitura com mais de 20 s está atrasada; com mais de 60 s, a máquina conta como "sem dados". */
const ATRASO_S = 20;
const SEM_DADOS_S = 60;

interface Valor {
  codigo: string;
  rotulo: string;
  texto: string;
}

@Component({
  selector: 'app-operacional',
  imports: [RouterLink, StatusMaquina],
  templateUrl: './operacional.html',
  styleUrl: './operacional.css',
})
export default class Operacional {
  protected readonly tempoReal = inject(TempoReal);
  protected readonly catalogo = inject(Catalogo);
  private readonly relogio = inject(Relogio);
  protected readonly agora = this.relogio.agora;

  constructor() {
    this.tempoReal.iniciar();
  }

  /** Máquinas sempre na mesma ordem (a da paleta), não na ordem de chegada. */
  protected readonly maquinas = computed(() => {
    const lista = this.tempoReal.agora()?.maquinas ?? [];
    return [...lista].sort((a, b) => this.ordem(a.codigo) - this.ordem(b.codigo));
  });

  protected readonly contagem = computed(() => {
    const c = { operacao: 0, atencao: 0, manutencao: 0, parada: 0, semDados: 0 };
    for (const m of this.maquinas()) {
      if (this.idade(m) > SEM_DADOS_S || !m.categoria) c.semDados++;
      else if (m.categoria === 'OPERACAO') m.alerta ? c.atencao++ : c.operacao++;
      else if (m.categoria === 'MANUTENCAO') c.manutencao++;
      else if (m.categoria === 'PARADA') c.parada++;
      else c.semDados++;
    }
    return c;
  });

  protected idade(m: MaquinaAgora): number {
    return m.ultimaLeitura ? (this.agora() - Date.parse(m.ultimaLeitura)) / 1000 : Infinity;
  }

  protected atraso(m: MaquinaAgora): 'ok' | 'atrasada' | 'parada' {
    const s = this.idade(m);
    return s > SEM_DADOS_S ? 'parada' : s > ATRASO_S ? 'atrasada' : 'ok';
  }

  /** Medidas primeiro (o que o operador vigia), contadores depois. */
  protected valores(m: MaquinaAgora, contadores: boolean): Valor[] {
    return Object.entries(m.valores)
      .filter(([codigo]) => this.catalogo.ehContador(codigo) === contadores)
      .map(([codigo, v]) => ({
        codigo,
        rotulo: this.catalogo.rotuloAtributo(codigo),
        texto: numero(v, contadores ? 3 : 2),
      }));
  }

  protected readonly haQuanto = haQuanto;
  protected readonly desde = desde;

  private ordem(codigo: string) {
    const i = this.catalogo.indiceCor(codigo);
    return i < 0 ? 99 : i;
  }
}
