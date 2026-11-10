# Telas (Angular 22)

Uma área por nível da pirâmide, cada uma carregada sob demanda:

| Pasta | Rota | Nível |
|---|---|---|
| `src/app/niveis/operacional` | `/operacional`, `/operacional/:maquina` | Operacional (ao vivo, SignalR) |
| `src/app/niveis/tatico` | `/tatico` | Tático (semanal e mensal) |
| `src/app/niveis/estrategico` | `/estrategico` | Estratégico (mensal e anual) |

As demais pastas dão suporte às três áreas:
- `src/app/dados`: acesso à API, canal ao vivo, cadastros e formatação.
- `src/app/graficos`: componente que envolve o ECharts e o tema de cores.
- `src/app/componentes`: peças reaproveitadas, como o selo de status.

```bash
npm install     # primeira vez
npm start       # desenvolvimento em http://localhost:4200 (proxy para o backend na 5080)
npm run build   # gera dist/painel/browser, servido pelo backend em http://localhost:5080
```

Veja [../docs/como-rodar.md](../docs/como-rodar.md).
