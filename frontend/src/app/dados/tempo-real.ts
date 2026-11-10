import { Injectable, inject, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { Agora, Api, Leitura, NovasLeituras } from './api';

/**
 * Canal ao vivo do nível operacional. O backend empurra cada coleta pelo
 * SignalR; se o canal cair, a tela continua atualizando por consulta a
 * cada 5 s até ele voltar.
 */
@Injectable({ providedIn: 'root' })
export class TempoReal {
  private readonly api = inject(Api);
  readonly agora = signal<Agora | null>(null);
  readonly conectado = signal(false);
  /** Leituras novas de todas as máquinas, na ordem em que chegaram. */
  readonly leituras = new Subject<Leitura[]>();

  private conexao?: HubConnection;

  iniciar() {
    if (this.conexao) return;
    this.consultar();
    this.conexao = new HubConnectionBuilder()
      .withUrl('/hubs/operacional')
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Warning)
      .build();
    this.conexao.on('leituras', (m: NovasLeituras) => {
      this.agora.set(m.agora);
      this.leituras.next(m.leituras);
    });
    this.conexao.onreconnecting(() => this.conectado.set(false));
    this.conexao.onreconnected(() => {
      this.conectado.set(true);
      this.consultar();
    });
    this.conexao.onclose(() => {
      this.conectado.set(false);
      setTimeout(() => this.conectar(), 5000);
    });
    this.conectar();
    setInterval(() => {
      if (!this.conectado()) this.consultar();
    }, 5000);
  }

  private conectar() {
    this.conexao
      ?.start()
      .then(() => this.conectado.set(true))
      .catch(() => setTimeout(() => this.conectar(), 5000));
  }

  private consultar() {
    this.api.agora().subscribe({ next: (a) => this.agora.set(a), error: () => {} });
  }
}
