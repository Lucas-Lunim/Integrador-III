# Comandos para iniciar o Projeto Integrador III

Execute os comandos no **PowerShell**.

## Painel com MySQL

O script de criação do banco `pi3` fica na pasta **`database`**, na raiz do projeto. Abra o arquivo [01_criar_banco_e_usuario.sql](C:/Users/lucas/OneDrive/Documentos/GitHub/Senai/Integrador-III/database/01_criar_banco_e_usuario.sql) pelo MySQL Workbench para configurar o banco na primeira execução.

Deixe o MySQL Server ligado. O backend usa a conexão salva em `backend/Pi3.Backend/appsettings.Local.json`.

### 1. Atualizar o endereço da API industrial

Quando o IP mudar, abra o [appsettings.json](C:/Users/lucas/OneDrive/Documentos/GitHub/Senai/Integrador-III/backend/Pi3.Backend/appsettings.json) em `backend/Pi3.Backend`. Na seção `Pi3`, edite o campo `ApiUrl`:

```json
"ApiUrl": "http://NOVO_IP:8080",
```

Substitua `NOVO_IP` pelo IP que te passaram. Se a porta da API também mudou, atualize `8080`. Salve o arquivo. Se o backend já estiver rodando, pressione `Ctrl+C` no terminal e inicie de novo com o comando abaixo. O painel continua acessível em `localhost:5080`.

### 2. Iniciar o painel

```powershell
Set-Location -LiteralPath 'C:\Users\lucas\OneDrive\Documentos\GitHub\Senai\Integrador-III\backend\Pi3.Backend'
dotnet .\bin\Debug\net10.0\Pi3.Backend.dll --urls http://localhost:5080
```

Esse comando usa a versão já compilada na pasta. Deixe o terminal aberto durante a coleta.

### 3. Abrir no navegador

Abra [localhost:5080](http://localhost:5080). Se preferir abrir pelo PowerShell, use **outro terminal**:

```powershell
Start-Process 'http://localhost:5080'
```

### 4. Conferir a conexão, se necessário

```powershell
# Verifica se o backend está respondendo e se inicializou
Invoke-RestMethod 'http://localhost:5080/api/saude'

# Substitua NOVO_IP pelo IP da API e ajuste a porta se necessário
Test-NetConnection -ComputerName NOVO_IP -Port 8080
```

Na resposta do primeiro comando, confira se `apiUrl` mostra o novo endereço. Se aparecer `pronto: False`, confira se o MySQL está ligado e se a conexão está configurada. Se a porta `5080` estiver ocupada por uma versão anterior, pressione `Ctrl+C` no terminal em que ela foi iniciada e rode o comando de inicialização novamente.

Para encerrar o painel, pressione `Ctrl+C` no terminal em que o backend está rodando.
