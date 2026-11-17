import { Injectable, signal } from '@angular/core';

/** Um "agora" que avança a cada segundo, para textos como "há 5 s". */
@Injectable({ providedIn: 'root' })
export class Relogio {
  readonly agora = signal(Date.now());

  constructor() {
    setInterval(() => this.agora.set(Date.now()), 1000);
  }
}
