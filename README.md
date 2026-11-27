# Projeto Integrador III — Sistema Integrado de Dados Industriais

Apresentação no Canva: https://canva.link/iy6zuny0z4os2a7

O sistema coleta em tempo real as leituras das máquinas da API industrial, grava no MySQL, sintetiza os dados com o passar do tempo e mostra três níveis de visão:

- **Operacional:** status e valores ao vivo de cada máquina.
- **Tático:** produção, perdas e disponibilidade por semana e por mês.
- **Estratégico:** tendências de meses e anos da planta.

| Pasta | Conteúdo |
|---|---|
| `backend/Pi3.Backend` | C# / .NET 10: coletor, sintetizador e API (Data Oriented Design) |
| `backend/Pi3.Simulador` | Simulador da API industrial, só para testes |
| `frontend/` | Angular 22 + TypeScript: uma área por nível |
| `database/` | Scripts do MySQL: banco, schema e limpeza |
| `docs/` | Documentação |
| `explore/` | Análise exploratória da API (`exploracao.html` + proxy `servidor.py`) |
| `PROJETO-INTEGRADOR-III.html` | Enunciado do projeto |

## Documentação

- [Guia rápido: como abrir o painel](docs/guia-abrir-o-app.md): passo a passo para o dia da apresentação
- [Como rodar](docs/como-rodar.md)
- [Arquitetura e DOD](docs/arquitetura.md)
- [Modelo de dados (DER atualizado)](docs/modelo-de-dados.md)
- [Tratativas de dados](docs/tratativas-de-dados.md): cada tratamento aplicado às leituras e a definição dos indicadores
- [Usuários de demonstração](docs/usuarios.md): contas, senhas e perfis do protótipo de login

## Início rápido

```bash
mysql -u root -p < database/01_criar_banco_e_usuario.local.sql   # uma vez
./rodar.sh 10.82.175.92                                            # IP atual da API → http://localhost:5080
```
