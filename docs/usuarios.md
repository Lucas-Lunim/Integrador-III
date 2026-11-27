# Usuários de demonstração

O painel tem um **protótipo de login**. Cada pessoa entra com um perfil, e o perfil define quais níveis da pirâmide ela vê.

## Contas

| Usuário | Senha | Nome no painel | Perfil | Níveis que vê |
|---|---|---|---|---|
| `operador` | `pi3` | Operador de máquina | Operador | Operacional |
| `tatico` | `pi3` | Supervisão | Tático | Operacional e Tático |
| `estrategico` | `pi3` | Diretoria | Estratégico | Operacional, Tático e Estratégico |

O usuário não diferencia maiúsculas de minúsculas; a senha, sim. As mesmas contas aparecem na tela de login: clicar no usuário preenche o formulário.

## Como funciona

- **Entrada:** quem abre o painel sem estar logado vai para `/login`. Depois de entrar, volta para a tela que tinha pedido.
- **Menu e tela inicial:** mostram só os níveis do perfil.
- **Acesso direto pelo endereço:** abrir um nível sem permissão (por exemplo, o operador digitando `/estrategico`) leva de volta à tela inicial, com um aviso.
- **Código das telas:** o navegador nem baixa o código de um nível sem permissão.
- **Sessão:** atualizar a página mantém o login; fechar a aba ou clicar em **Sair** encerra.

## Limitações do protótipo

Isto é uma demonstração de perfis, **não um controle de acesso seguro**:

- **As contas ficam no código do frontend**, em `frontend/src/app/acesso/sessao.ts`. Quem abrir o código do site vê as senhas.
- **O backend não confere quem está pedindo.** As rotas `/api/...` e o canal ao vivo (SignalR) continuam abertos: quem chamar a API direto recebe os dados de qualquer nível.
- **O banco de dados não foi alterado.**

Para virar um controle de acesso de verdade, os próximos passos seriam:
1. Criar uma tabela de usuários no banco, com a senha guardada em hash (bcrypt ou Argon2), nunca em texto.
2. Fazer o login no backend e devolver um token ou cookie de sessão.
3. Exigir o perfil em cada rota `/api` e no hub SignalR.

## Como mudar as contas

As contas estão em `CONTAS_DEMONSTRACAO`, e os níveis de cada perfil em `NIVEIS_DO_PERFIL`, os dois em `frontend/src/app/acesso/sessao.ts`. Depois de mudar, compile de novo as telas (`npm run build` em `frontend/`, ou `./rodar.sh`, que recompila sozinho).
