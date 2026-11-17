# Como iniciar o Projeto Integrador III

Use o **PowerShell** na pasta extraída do projeto. Você precisa do **SDK do .NET 10**, do **MySQL Server** e de uma versão do **Node.js** compatível com o Angular do projeto, como a linha 24 a partir da versão 24.15.

## 1. Preparar o MySQL

No MySQL Workbench, abra `database/01_criar_banco_e_usuario.sql` com uma conta administrativa. Substitua `__SENHA__` por uma senha local e execute o script para criar o banco `pi3` e o usuário `pi3_app`. O backend aplica o script `database/02_schema.sql` ao inicializar.

Crie a configuração local na pasta `backend/Pi3.Backend` na primeira execução:

```powershell
Copy-Item .\backend\Pi3.Backend\appsettings.Local.exemplo.json .\backend\Pi3.Backend\appsettings.Local.json
```

Edite `appsettings.Local.json` e preencha `ConnectionStrings.Banco` com a senha do usuário `pi3_app`.

## 2. Configurar o IP da API industrial

Em `backend/Pi3.Backend/appsettings.json`, altere `ApiUrl` na seção `Pi3`. Use o endereço e a porta informados para a API:

```json
"ApiUrl": "http://NOVO_IP:8080",
```

Repita esse ajuste sempre que o endereço mudar e reinicie o backend para aplicar a configuração.

## 3. Compilar e executar

```powershell
Set-Location .\frontend
npm.cmd ci
npm.cmd run build
Set-Location ..
dotnet run --project .\backend\Pi3.Backend
```

Deixe o terminal aberto e acesse [localhost:5080](http://localhost:5080). Para encerrar, pressione `Ctrl+C` no terminal.

Em outro terminal, confira a inicialização do backend e o endereço da API usado na coleta:

```powershell
Invoke-RestMethod 'http://localhost:5080/api/saude'
```
