# Guia rápido: como abrir o painel

Passo a passo para abrir o sistema no notebook da apresentação. Os detalhes técnicos (instalação, banco, modo demonstração) estão em [como-rodar.md](como-rodar.md).

**Endereço atual da API das máquinas:** `http://10.82.175.92:8080` (já configurado).

## 1. Conectar na rede do laboratório

O notebook precisa estar na mesma rede das máquinas. Para conferir, abra no navegador:

**http://10.82.175.92:8080/maquinas**

Se aparecer uma lista em JSON, a rede está certa. Se a página não carregar, troque de rede (ou confira o IP com o professor) antes de seguir.

## 2. Conferir o banco de dados

Num terminal:

```bash
systemctl is-active mysqld
```

Tem que responder `active`. Se responder outra coisa, ligue o banco com `sudo systemctl start mysqld`.

## 3. Subir o sistema

Na pasta do projeto:

```bash
cd ~/Documents/Senai/PI-III/Integrador-III
./rodar.sh
```

O script confere a API, recompila as telas se algo mudou e sobe o backend. Ele está pronto quando aparecer:

```
Banco pronto: ... Coletando de http://10.82.175.92:8080
```

**Deixe esse terminal aberto** enquanto usa o painel. Para encerrar, aperte `Ctrl+C` nele.

## 4. Abrir o painel e entrar

1. Abra **http://localhost:5080** no navegador.
2. Entre com uma das contas abaixo. Elas também aparecem na própria tela de login: clicar no usuário preenche o formulário.

| Usuário | Senha | Vê |
|---|---|---|
| `operador` | `pi3` | Operacional |
| `tatico` | `pi3` | Operacional e Tático |
| `estrategico` | `pi3` | Operacional, Tático e Estratégico |

Mais sobre os perfis em [usuarios.md](usuarios.md).

## 5. Conferir se está coletando

No canto superior direito do painel, a etiqueta tem que mostrar **"Coleta ok · há N s"**, com o ponto verde.

O nível **Operacional** mostra os dados ao vivo logo de cara. Os níveis **Tático** e **Estratégico** dependem dos resumos, que se acumulam com o tempo de coleta: cada hora coletada vira resumo 2 a 3 minutos depois que ela termina.

## Se algo der errado

| O que aparece | O que fazer |
|---|---|
| "A API não respondeu" no terminal, ou etiqueta vermelha "A API não respondeu a tempo" | O notebook não está na rede do laboratório ou o IP mudou. Refaça o passo 1. |
| Etiqueta "Aguardando o banco de dados" | O MySQL está desligado: `sudo systemctl start mysqld`. O sistema reconecta sozinho. |
| Etiqueta "Backend fora do ar" | O terminal do `./rodar.sh` foi fechado. Rode de novo o passo 3. |
| "A porta 5080 está em uso" ao rodar o script | O sistema já está aberto em outro terminal. Use esse, ou encerre-o com `Ctrl+C`. |
| A página `localhost:5080` não abre | Espere a mensagem "Banco pronto" no terminal. Na primeira vez, a compilação leva cerca de 1 minuto. |

## Se o IP da API mudar de novo

- **Só para esta vez:** `./rodar.sh NOVO_IP` (a porta 8080 é assumida; se for outra, use `./rodar.sh NOVO_IP:PORTA`).
- **Para sempre:** troque o `ApiUrl` em `backend/Pi3.Backend/appsettings.json` e rode `./rodar.sh` normalmente.
