# Como rodar

Passo a passo para rodar o sistema no notebook (Fedora) da apresentação.

Para só abrir o painel, sem os detalhes, use o [guia rápido](guia-abrir-o-app.md).

## O que precisa estar instalado

| Ferramenta | Versão | Como conferir |
|---|---|---|
| .NET SDK | 10 | `dotnet --version` |
| MySQL Server | 8.4 | `systemctl is-active mysqld` |
| Node.js | 26 LTS | `node --version` (instalado em `~/.local/share/nodejs`, com atalhos em `~/.local/bin`) |
| Angular CLI | 22 | `ng version` |

## 1. Criar o banco (uma vez só)

O arquivo `database/01_criar_banco_e_usuario.local.sql` já está pronto. Ele tem a senha gerada para esta máquina e fica fora do Git. Rode como root do MySQL:

```bash
mysql -u root -p < database/01_criar_banco_e_usuario.local.sql
```

Isso cria o banco `pi3` e o usuário `pi3_app`. As tabelas são criadas pelo backend na primeira vez que ele sobe.

> A mesma senha está em `backend/Pi3.Backend/appsettings.Local.json`, também fora do Git. Em outra máquina, copie `appsettings.Local.exemplo.json` para `appsettings.Local.json`, troque `__SENHA__` por uma senha no `01_criar_banco_e_usuario.sql` e salve a cópia como `.local.sql`.

## 2. Conferir o endereço da API

O IP da API industrial muda com frequência. Ajuste em `backend/Pi3.Backend/appsettings.json`:

```json
"Pi3": { "ApiUrl": "http://10.82.175.92:8080" }
```

Ou, só para uma execução: `Pi3__ApiUrl=http://NOVO_IP:8080 dotnet run --project Pi3.Backend`.

## 3. Compilar as telas (depois de cada mudança no frontend)

```bash
cd frontend
npm install        # só na primeira vez
npm run build
```

## 4. Subir o sistema

Para o uso real, use o script na raiz do repositório:

```bash
./rodar.sh                 # usa o ApiUrl do appsettings.json
./rodar.sh 10.82.175.92    # ou informe o IP atual da API
```

O script:
- avisa se a API não responder;
- recompila as telas se o frontend mudou;
- sobe o backend em modo Release.

Sem o script, o equivalente é:

```bash
cd backend
dotnet run --project Pi3.Backend
```

Abra **http://localhost:5080**. O backend coleta a API, grava no banco, gera os resumos e serve as telas, tudo no mesmo processo. Para conferir a situação: http://localhost:5080/api/saude.

O painel pede login. As contas de demonstração (`operador`, `tatico` e `estrategico`) estão em [usuarios.md](usuarios.md).

### Modo demonstração

```bash
dotnet run --project Pi3.Backend --launch-profile demo
```

Encurta os ciclos de síntese (15 s) e a retenção (brutos por 30 min), para mostrar os resumos sendo gerados e o detalhe antigo sendo apagado. **Não use no dia a dia:** com esses valores o detalhe bruto some rápido.

## Desenvolvendo as telas

Com o backend rodando, num segundo terminal:

```bash
cd frontend
npm start
```

Abra http://localhost:4200. As telas recarregam sozinhas a cada mudança, e o proxy (`proxy.conf.json`) encaminha `/api` e `/hubs` para o backend na porta 5080.

## Testando sem a API real (simulador)

O simulador imita a API do professor (mesmas rotas e formato), com dados **fictícios** calibrados pelas leituras reais.

```bash
cd backend
dotnet run --project Pi3.Simulador -- --falhas --reinicio-minutos 10
# noutro terminal:
Pi3__ApiUrl=http://localhost:8090 dotnet run --project Pi3.Backend
```

- `--falhas` injeta problemas de qualidade: campo faltando, valor nulo, número como texto, status com acento.
- `--reinicio-minutos N` simula a API reiniciando: os ids voltam a 1 e os contadores zeram.

> **Atenção:** o simulador grava dados fictícios no banco. Depois de testar, pare o backend e limpe as leituras, mantendo os cadastros:
>
> ```bash
> mysql -u root -p pi3 < database/03_apagar_leituras.sql
> ```

## Problemas comuns

| Sintoma | Causa provável |
|---|---|
| Topo da tela: "API inacessível" | IP errado, outra rede ou API desligada. Teste `curl http://IP:8080/status`. |
| Topo da tela: "Aguardando o banco de dados" | MySQL parado (`sudo systemctl start mysqld`) ou senha diferente entre o `.local.sql` e o `appsettings.Local.json`. |
| `Connection string 'Banco' não configurada` ao iniciar | Falta o `backend/Pi3.Backend/appsettings.Local.json`. |
| A página em `localhost:5080` diz que o frontend não foi compilado | Rode `npm run build` em `frontend/`. |
| `Address already in use` | Outro backend já está rodando na porta 5080. |
| Tático ou estratégico com cobertura baixa | Normal no começo: os resumos só cobrem o tempo em que o sistema esteve coletando. |
