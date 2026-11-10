# Tratativas de dados

Este documento registra **cada tratamento** aplicado às leituras da API industrial. O objetivo é que qualquer pessoa consiga interpretar corretamente os números das telas e dos resumos.

**Princípio geral:** o valor recebido nunca é alterado nem inventado.
- Nada é preenchido, interpolado ou apagado por "parecer errado".
- Quando uma leitura precisa de tratamento, isso fica **marcado** na coluna `leitura.qualidade` (ver [Marcas de qualidade](#marcas-de-qualidade)).
- A regra aplicada é a descrita abaixo.

As evidências vêm da análise exploratória (`explore/exploracao.html`) e do arquivo `leituras_2026-11-03-11-28-37.csv`: 492 leituras coletadas em 26/10 e 03/11/2026.

## Resumo

| Código | Tratativa | Regra em uma linha | Marca |
|---|---|---|---|
| [T01](#t01--leitura-sem-máquina) | Leitura sem máquina | Descartada e contada | — |
| [T02](#t02--cadastro-automático) | Cadastro automático | Código novo da API vira cadastro; máquina nova entra no Setor1 | — |
| [T03](#t03--horário-oficial-da-leitura) | Horário oficial | Vale o `timestamp` da API; inválido → horário da coleta | 64 |
| [T04](#t04--leituras-repetidas) | Leituras repetidas | Só entra leitura mais nova que a última da máquina | — |
| [T05](#t05--lacunas-e-primeira-leitura) | Lacunas | Intervalo > 60 s não é contínuo: sem duração e sem incremento | 1, 2 |
| [T06](#t06--contadores) | Contadores | Vale a variação; queda = reinício e o incremento é descartado | 4 |
| [T07](#t07--status) | Status | Normalizado e agrupado em operação / manutenção / parada | 8 |
| [T08](#t08--valores-numéricos) | Valores | Só números; texto numérico aceito; nulo e texto livre ignorados | 32 |
| [T09](#t09--atributo-ausente) | Atributo ausente | Marcado, nunca preenchido | 16 |
| [T10](#t10--tipo-da-máquina) | Tipo da máquina | Guardado uma vez na máquina; divergência marcada | 128 |
| [T11](#t11--tempo-em-cada-status) | Tempo em status | O intervalo conta para o status da leitura que o fecha | — |
| [T12](#t12--síntese-e-retenção) | Síntese e retenção | Bruto 7 dias → hora 90 dias → dia, semana, mês, ano para sempre | — |
| [T13](#t13--fuso-e-calendário) | Fuso e calendário | Grava em UTC; dia, semana e mês no horário de Brasília | — |
| [T14](#t14--unidades) | Unidades | Só as que o nome do campo indica | — |

## O que os dados mostraram

| Fato observado no CSV | Consequência |
|---|---|
| Cada máquina gera uma leitura a cada **5,0 s** (mediana). A API guarda só os últimos ~60 s. | O coletor consulta a cada 5 s. Cada leitura chega ~12 vezes (T04). |
| Cada tipo de máquina envia um conjunto diferente de campos. Exemplo: só o torno manda `rotacao`, só o compressor manda `consumo_kw`. | Modelo atributo-valor. A ausência de um campo que o tipo não mede não é falta (T09). |
| O `id` volta a 1 depois do 4227: a API reiniciou entre 26/10 e 03/11. No mesmo ponto, os contadores voltaram a valores menores (torno: produção 14.652 → 1.872). | O `id` não serve como chave (T04). Uma queda de contador é reinício (T06). |
| Faltam 10 ids em 6 trechos (2379–2380, 2415–2416, 4210, 4226, 148–149, 220–221). | Leituras perdidas antes da coleta. Viram lacuna, não são preenchidas (T05). |
| Lacunas de tempo de 53 s, 52 min e 7,9 dias entre leituras da mesma máquina. | Limite de continuidade de 60 s (T05). |
| `coletado_em` menos `timestamp` vai de −3,1 s a +26 s. Em 44 das 492 leituras a coleta aparece **antes** da leitura. | Os relógios da API e do coletor diferem. Vale o relógio da API (T03). |
| Status: `OPERANDO` e `ATENCAO`. O robô aparece em `ATENCAO` em 100% das leituras. | `ATENCAO` não é um dos três status do negócio: vira "operação com alerta" (T07). |
| `producao`, `defeitos`, `ciclos`, `desgaste`, `rejeitados` e `horas_operacao` só sobem. A única queda coincide com o reinício da API. | São contadores acumulados (T06). |
| Prensa: `producao = ciclos − defeitos` em todas as leituras. | Os ciclos incluem as peças com defeito. Isso define a taxa de perdas ([Indicadores](#definição-dos-indicadores)). |
| Nenhuma leitura deixou de trazer um campo que o seu tipo envia. | A marca 16 deve ser rara com a API real (T09). |

## Tratativas

### T01 — Leitura sem máquina
- **Problema:** um item da lista sem o campo `maquina` não pode ser atribuído a nada.
- **Regra:** o item é descartado e contado em `/api/saude` → `coleta.invalidas`.
- **Código:** `Transformacoes/Decodificacao.cs`.

### T02 — Cadastro automático
- **Problema:** a API pode trazer máquinas, tipos, status ou campos que o banco ainda não conhece. O CSV mostra a ESTEIRA, o ROBÔ e a EMBALADORA aparecendo só em 03/11.
- **Regra:** o código novo é cadastrado na hora.
  - Máquina nova entra no **Setor1**, combinado para a apresentação.
  - Atributo novo entra como **MEDIDA** (veja T06).
  - Status novo entra na categoria **DESCONHECIDO** (veja T07).
  - Cada par (tipo, atributo) visto é registrado em `atributo_tipo_maquina`. É ali que o sistema aprende o que cada tipo envia.
- **Código:** `Persistencia/Banco.cs` → `GarantirCadastrosAsync`.

### T03 — Horário oficial da leitura
- **Problema:** a leitura tem dois horários: o `timestamp` gerado pela API e o momento em que o coletor a recebeu. Os relógios não batem: a diferença vai de −3,1 s a +26 s.
- **Regra:**
  - O horário oficial é o **`timestamp` da API**, gravado em UTC com milissegundos (`leitura.instante`). O horário da coleta fica em `leitura.coletado_em`, só para auditoria.
  - Se o `timestamp` estiver ausente, for inválido, for anterior a 2000 ou estiver **mais de um dia à frente** do relógio do coletor, usa-se o horário da coleta e a leitura recebe a **marca 64**.
  - Motivo do limite futuro: uma data absurda no futuro "travaria" a máquina, porque toda leitura mais antiga que a última é ignorada (T04).
- **Código:** `Transformacoes/Decodificacao.cs` → `TentarInstante`.

### T04 — Leituras repetidas
- **Problema:** a cada consulta a API devolve a janela inteira dos últimos ~60 s. Com coleta a cada 5 s, a mesma leitura chega cerca de 12 vezes. O `id` da API não serve para separar leituras novas: ele **reinicia** quando a API reinicia.
- **Regra:**
  - Uma leitura só é aceita se for **mais nova que a última leitura aceita da mesma máquina**.
  - No banco, a chave de `leitura` é (instante, máquina), com `INSERT IGNORE`. Regravar um lote não duplica nada.
  - O `id` da API é guardado (`id_api`) só para consulta.
- **Código:** `Transformacoes/Derivacao.cs`; `Persistencia/BancoLeituras.cs`.

### T05 — Lacunas e primeira leitura
- **Problema:** há trechos sem leitura (coletor desligado, ids perdidos). Calcular a variação de um contador por cima de uma lacuna joga num único instante tudo o que aconteceu durante ela. Exemplo do CSV: +10.952 peças de uma vez depois de 52 min sem coleta.
- **Regra:**
  - Duas leituras da mesma máquina são **contínuas** se o intervalo entre elas for de até **60 s** (configurável: `Pi3:LimiteLacunaSegundos`).
  - Sem continuidade, `duracao_ms` fica **NULL**, nenhum incremento é calculado e a leitura recebe a **marca 2** (lacuna antes).
  - A primeira leitura de cada máquina recebe a **marca 1**.
  - **Não há interpolação.** O período sem dados aparece nas telas como cobertura menor que 100%.
- **Efeito:** totais como produção e horas refletem **só o tempo observado**. O indicador "Cobertura dos dados" diz quanto do período foi observado.
- **Código:** `Transformacoes/Derivacao.cs`.

### T06 — Contadores
- **Problema:** alguns campos são acumulados desde o início da máquina, como a produção. A média deles não tem significado; o que importa é quanto subiram no período.
- **Regra:**
  - Os atributos têm natureza **MEDIDA** (valor instantâneo) ou **CONTADOR** (acumulado).
  - São contadores: `producao`, `defeitos`, `rejeitados`, `ciclos`, `desgaste`, `horas_operacao`, `falhas` e `paradas`. A classificação vem do comportamento no CSV: só sobem.
  - `rotacao` cai devagar com o desgaste, mas é uma medida.
  - Para contadores, cada leitura contínua (T05) guarda `incremento = valor − valor anterior`.
  - Uma **queda** é tratada como reinício do contador: aconteceu no reinício da API e pode acontecer quando a manutenção zera o desgaste. Nesse caso o incremento é **descartado** (desconhecido) e a leitura recebe a **marca 4**.
  - Os resumos somam os incrementos.
- **Por que descartar e não somar o novo valor:** depois de um reinício não se sabe de onde o contador recomeçou. O CSV mostra contadores que voltaram para 1.872, não para zero. Perde-se no máximo um intervalo de ~5 s.
- **Código:** `Transformacoes/Derivacao.cs`; natureza em `database/02_schema.sql` (tabela `atributo`).

### T07 — Status
- **Problema:** o negócio tem três estados: em operação, em manutenção e parada. A API usa códigos em texto e já trouxe um quarto (`ATENCAO`). Grafias podem variar.
- **Regra:**
  - O código é **normalizado**: maiúsculas, sem acento, espaços e hífens viram `_`. Assim, "Em manutenção" vira `EM_MANUTENCAO`.
  - Cada código pertence a uma **categoria** (`status_maquina.categoria`):

    | Código | Categoria | Alerta |
    |---|---|---|
    | `OPERANDO` | operação | não |
    | `ATENCAO` | operação | **sim** |
    | `MANUTENCAO`, `EM_MANUTENCAO` | manutenção | não |
    | `PARADA`, `PARADO` | parada | não |
    | `FALHA` | parada | sim (hipótese: ainda não apareceu na API) |
    | `DESCONHECIDO` | desconhecido | não |

  - **Leitura sem status:** vira `DESCONHECIDO` e recebe a **marca 8**.
  - **Código nunca visto:** é cadastrado na categoria DESCONHECIDO e também recebe a **marca 8**, até alguém classificá-lo no banco.
- **Efeito:** `ATENCAO` conta como tempo **em operação**: a máquina está produzindo. As horas em atenção também aparecem à parte. O tempo em status desconhecido não entra na disponibilidade.
- **Código:** `Transformacoes/Decodificacao.cs` → `NormalizarStatus`; `Transformacoes/Derivacao.cs`.

### T08 — Valores numéricos
- **Regra:**
  - Todo campo que não seja `id`, `timestamp`, `maquina`, `tipo` ou `status` é tratado como atributo numérico.
  - Texto com número é aceito, com vírgula ou ponto: `"12,5"` vira 12,5.
  - Nulo, booleano e texto livre são **ignorados**, e a leitura recebe a **marca 32**.
  - Os números são gravados **como vieram** (DOUBLE). Ruído de ponto flutuante como `5.490001` é mantido; o arredondamento acontece só na tela.
- **Código:** `Transformacoes/Decodificacao.cs` → `TentarNumero`.

### T09 — Atributo ausente
- **Problema:** é preciso separar duas situações:
  - **ausência estrutural:** a esteira não mede pressão, o que é normal;
  - **falta:** o torno deixou de mandar a temperatura.
- **Regra:**
  - Cada tipo tem a sua lista de atributos esperados, aprendida das próprias leituras (T02).
  - Quando falta um atributo esperado, a leitura recebe a **marca 16**. **Nada é preenchido.**
  - Nas médias, a leitura simplesmente não conta para aquele atributo.
- **Código:** `Transformacoes/Derivacao.cs`.

### T10 — Tipo da máquina
- **Regra:** o campo `tipo` vem repetido em toda leitura, mas é guardado **uma vez**, na máquina (normalização). Se uma leitura trouxer um tipo diferente do cadastrado, ela recebe a **marca 128** e o cadastro não muda.
- **Código:** `Transformacoes/Derivacao.cs`.

### T11 — Tempo em cada status
- **Regra:**
  - O intervalo entre duas leituras contínuas conta para o status informado na **leitura que fecha o intervalo**.
  - O erro máximo é de um intervalo, ~5 s, a cada troca de status.
  - Lacunas (T05) não contam para nenhum status.
- **Código:** `Transformacoes/Sintese.cs` → `DeBrutos`.

### T12 — Síntese e retenção
- **Problema:** são ~17 mil leituras por máquina por dia. Os níveis altos precisam de anos de dados, mas não do detalhe de cada leitura.
- **Regra:**
  - **Cadeia de síntese:** leituras brutas → resumo por **hora** → **dia** → **semana** e **mês** → **ano**.
  - **Formato do resumo:** todo resumo guarda quantidade, soma, soma dos quadrados, mínimo, máximo, primeiro valor, último valor e soma dos incrementos. São campos "somáveis": o dia sai exato das horas, sem precisar dos brutos. Médias e desvios também saem exatos.
  - **Fechamento:**
    - Uma hora fecha 2 min depois de terminar (carência para atrasos e diferença de relógio).
    - Os outros períodos fecham quando todo o nível de baixo já fechou.
    - Períodos ainda abertos são recalculados a cada minuto, para mostrar o período atual.
  - **Retenção (configurável):**

    | Dado | Guardado por |
    |---|---|
    | Leituras brutas | 7 dias |
    | Resumos por hora | 90 dias |
    | Resumos de dia, semana, mês e ano | para sempre |

    **Nada é apagado antes de estar num resumo fechado.**
- **Código:** `Sistemas/Sintetizador.cs`; `Transformacoes/Sintese.cs`.

### T13 — Fuso e calendário
- **Regra:**
  - Todo horário é gravado em **UTC**.
  - Os limites de dia, semana, mês e ano seguem o **horário de Brasília (UTC−3)**. É um deslocamento fixo: o Brasil não tem horário de verão desde 2019.
  - A semana vai de **segunda a domingo**.
- **Código:** `Dados/Tempo.cs`; `Pi3:FusoMinutos`.

### T14 — Unidades
- **Regra:** a API não informa unidades. Só foram preenchidas as que o próprio nome do campo indica: `consumo_kw` → kW e `horas_operacao` → h. As demais ficam em branco (`atributo.unidade` = NULL) até alguém confirmar.

## Marcas de qualidade

`leitura.qualidade` é uma soma de bits. Uma leitura com valor 48 tem as marcas 16 + 32.

| Bit | Significado | Tratativa |
|---|---|---|
| 1 | Primeira leitura conhecida da máquina | T05 |
| 2 | Lacuna antes desta leitura (> 60 s) | T05 |
| 4 | Algum contador diminuiu (reinício) | T06 |
| 8 | Status ausente ou não classificado | T07 |
| 16 | Faltou atributo que o tipo envia | T09 |
| 32 | Campo nulo ou não numérico ignorado | T08 |
| 64 | Timestamp inválido: usado o horário da coleta | T03 |
| 128 | Tipo diferente do cadastrado | T10 |

```sql
-- Quantas leituras tiveram cada tratamento
SELECT qualidade, COUNT(*) FROM leitura GROUP BY qualidade;
-- Leituras com reinício de contador
SELECT * FROM leitura WHERE qualidade & 4;
```

## Definição dos indicadores

Todos os indicadores são calculados a partir dos resumos (`Transformacoes/Indicadores.cs`).

| Indicador | Definição |
|---|---|
| **Produção** | Soma dos incrementos de `producao` (peças boas). O robô de soldagem não tem `producao`: as tabelas mostram os ciclos dele. |
| **Perdas** | Soma dos incrementos de `defeitos` + `rejeitados`. |
| **Taxa de perdas** | Perdas ÷ peças processadas. Peças processadas = produção + perdas; para máquina sem produção, os ciclos. Na prensa isso é exatamente os ciclos, porque `producao = ciclos − defeitos`. |
| **Disponibilidade** | Horas em operação (inclui atenção) ÷ (horas em operação + manutenção + parada). O tempo sem status conhecido e as lacunas ficam de fora. |
| **Horas observadas** | Soma das durações entre leituras contínuas. |
| **Cobertura dos dados** | Horas observadas ÷ (horas decorridas do período × nº de máquinas). Abaixo de 50%, as telas avisam. |
| **Desgaste +** | Soma dos incrementos de `desgaste` no período. |
| **Falhas novas** | Soma dos incrementos de `falhas`. |
| **Temperatura e vibração médias** | Soma dos valores ÷ quantidade de leituras (média ponderada). O máximo é o maior máximo do período. |

## Observações ainda sem tratativa

Ficam registradas para quando os limites operacionais forem definidos.

| Observação | Evidência no CSV |
|---|---|
| A temperatura do robô parece **limitada em 90,0** (teto do sensor ou da simulação). | 17 das 47 leituras exatamente em 90,0. |
| A carga da esteira parece ter **piso em 30** e a velocidade da embaladora **piso em 20**. | 7 e 5 leituras exatamente nesses valores. |
| Contadores que nunca mudaram: `horas_operacao` = 1200 e `falhas` = 0 no compressor; `paradas` = 1 na esteira; `falhas` = 1 no robô e na embaladora. | Valores constantes em todas as leituras. |
| `horas_operacao` de esteira, robô e embaladora fica entre 0,0001 e 0,0005 e sobe ~0,0000014 por leitura: a **escala é incerta**. | — |
| A rotação do torno cai com o desgaste: 3.176 → 3.013 enquanto o desgaste foi de 4,7 → 37. Temperatura e vibração também sobem com o desgaste. | — |
